using System.Globalization;
using HarmonyLib;
using LeanAndMeanCards.Utils;
using UnityEngine;

namespace LeanAndMeanCards.Patches
{
    /// <summary>
    /// Un-sticks a bullet that Target BOUNCE parked and never released.
    ///
    /// Vanilla's BounceEffectRetarget.DelayMove switches the bullet off on contact
    /// (move.enabled = false), waits 0.1s, and then only the bullet's *owner* calls
    /// ActuallyDoBounce, which hands every client a new velocity over ChildRPC
    /// ("TargetBounce" -> RPCA_RecieveFunction). Nothing in that chain has a fallback:
    ///
    ///   * if the ChildRPC is dropped — the log for the reported match is full of
    ///     "Received RPC RPCA_RecieveFunction ... this PhotonView does not exist" — the
    ///     receiving client's copy of the bullet stays switched off forever;
    ///   * ActuallyDoBounce aims with `direction * move.velocity.magnitude`, so if the
    ///     magnitude has been zeroed in the meantime the bullet resumes at a standstill.
    ///
    /// Either way the bullet loses all velocity on its first bounce. This watchdog waits
    /// well past the vanilla release point and then restores the plain reflected velocity,
    /// which is what the bounce would have been without the retarget. If the real
    /// TargetBounce message shows up first there is nothing to do.
    /// </summary>
    [HarmonyPatch(typeof(BounceEffectRetarget), nameof(BounceEffectRetarget.DoBounce))]
    internal static class TargetBounceStallPatch
    {
        private static void Postfix(BounceEffectRetarget __instance, HitInfo hit)
        {
            try
            {
                if (__instance == null || hit == null) return;
                var move = __instance.GetComponentInParent<MoveTransform>();
                if (move == null) return;

                // DoBounce is raised from RayHitReflect.reflectAction, which runs *before*
                // RayHitReflect writes the reflected velocity, so move.velocity is still the
                // incoming vector here and this is the same reflection the game is about to
                // perform.
                var fallback = Vector2.Reflect(move.velocity, hit.normal);
                var watchdog = move.gameObject.GetComponent<TargetBounceWatchdog>()
                               ?? move.gameObject.AddComponent<TargetBounceWatchdog>();
                watchdog.Arm(move, fallback);

                // Incoming speed at the moment of the bounce. Compared against the
                // watchdog's later reading this shows how much velocity the bounce lost.
                Diag.Event("bounce.armed", "in=" + ((Vector2)move.velocity).magnitude
                                                    .ToString("0.##", CultureInfo.InvariantCulture));
            }
            catch
            {
                // A bullet that bounces normally is the common case; never break it.
            }
        }
    }

    internal sealed class TargetBounceWatchdog : MonoBehaviour
    {
        // Vanilla releases at 0.1s on the same (timeScale-driven) clock. This is late enough
        // that a bounce which is merely slow — network latency, slow motion — is never cut
        // short, and early enough that a stuck bullet is not left hanging in the arena.
        private const float Grace = 0.6f;
        private const float StoppedSqr = 0.01f;

        private static float _lastLog;

        private MoveTransform _move;
        private Vector2 _fallback;
        private float _deadline;
        private bool _armed;

        internal void Arm(MoveTransform move, Vector2 fallback)
        {
            _move = move;
            if (fallback.sqrMagnitude > StoppedSqr) _fallback = fallback;
            _deadline = Time.time + Grace;
            _armed = true;
        }

        private void Update()
        {
            if (!_armed) return;
            if (_move == null)
            {
                _armed = false;
                return;
            }

            if (Time.time < _deadline) return;
            _armed = false;

            var stalled = !_move.enabled;
            var speed = ((Vector2)_move.velocity).magnitude;
            var stopped = speed * speed < StoppedSqr;

            if (!stalled && !stopped)
            {
                // The bounce completed on its own. Record how much speed survived it: a
                // healthy reflect keeps roughly the incoming magnitude, and a partial loss
                // — the reported symptom — shows up here as a ratio well under 1 that the
                // stalled/stopped branch below would never have caught.
                var expected = _fallback.magnitude;
                if (expected > 0.01f)
                {
                    var ratio = speed / expected;
                    if (ratio < 0.75f)
                    {
                        Diag.Event(
                            "bounce.velocityLoss",
                            "out=" + speed.ToString("0.##", CultureInfo.InvariantCulture)
                            + " expected=" + expected.ToString("0.##", CultureInfo.InvariantCulture)
                            + " ratio=" + ratio.ToString("0.##", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        Diag.Count("bounce.ok");
                    }
                }

                return;
            }

            if (_fallback.sqrMagnitude < StoppedSqr)
            {
                Diag.Count("bounce.noFallback");
                return;
            }

            Diag.Event("bounce.watchdogFired", "stalled=" + stalled + " stopped=" + stopped);

            if (stopped) _move.velocity = _fallback;
            if (stalled) _move.enabled = true;

            if (Time.time - _lastLog > 2f)
            {
                _lastLog = Time.time;
                Plugin.Instance?.Log(
                    $"Target BOUNCE bullet released by watchdog (stalled={stalled} stopped={stopped}).");
            }
        }
    }
}
