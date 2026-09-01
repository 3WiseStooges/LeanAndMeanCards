using System.Globalization;
using System.Reflection;
using HarmonyLib;
using LeanAndMeanCards.Utils;
using UnityEngine;

namespace LeanAndMeanCards.Patches
{
    /// <summary>
    /// Vanilla explosions (Timed Detonation, Explosive, etc.) and NetworkPhysicsObject
    /// BulletPush apply rigidbody impulses to anything in the radius. On the host that
    /// includes live bouncing bullets, which kills their vertical speed and looks like
    /// Drop Grenade. Dynamite already skipped its own blast; this covers the rest.
    ///
    /// Only Impulse is filtered. MoveTransform still applies its own gravity each frame.
    ///
    /// NOTE (desync investigation): the two Rigidbody2D patches below are engine-wide — every
    /// Impulse in the game routes through them, ours or not. If IsProjectileBody ever
    /// resolves differently on two peers, the impulse is dropped on one machine and kept on
    /// the other, and the two simulations diverge from that frame on. That is the leading
    /// theory for the reported "velocity lost on bounce" desync, so every decision here is
    /// now counted, and Diagnostics.FilterProjectileImpulses turns the whole filter off for a
    /// controlled A/B without needing a rebuild.
    /// </summary>
    internal static class ImpulseFilter
    {
        private static bool _active = true;
        private static bool _cached;

        /// <summary>False makes all four patches below pass straight through to vanilla.</summary>
        internal static bool Active
        {
            get
            {
                if (!_cached) Refresh();
                return _active;
            }
        }

        internal static void Refresh()
        {
            try
            {
                _active = Plugin.Configs?.FilterProjectileImpulses?.Value ?? true;
            }
            catch
            {
                _active = true;
            }

            _cached = true;
        }

        /// <summary>
        /// Shared decision for all four sites. Returns true to run the original method.
        /// Records what was dropped so a host log and a client log can be compared: for the
        /// same round the suppressed counts should be identical, and are not if this is the
        /// desync source.
        /// </summary>
        internal static bool Allow(string site, Transform t, Vector2 force)
        {
            var isProjectile = DynamiteBlast.IsProjectileBody(t);
            if (!isProjectile) return true;

            if (!Active)
            {
                Diag.Count("impulse." + site + ".passthrough");
                return true;
            }

            if (Diag.Enabled)
            {
                Diag.Event(
                    "impulse." + site + ".suppressed",
                    Diag.Describe(t) + " mag=" + force.magnitude.ToString("0.##", CultureInfo.InvariantCulture));

                // A suppressed impulse on a player is knockback that never happened. If this
                // ever fires, positions diverge between peers and the filter is too broad.
                if (t != null && t.GetComponentInParent<Player>() != null)
                {
                    Diag.Event("impulse." + site + ".suppressed.PLAYER", Diag.Describe(t));
                }
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(NetworkPhysicsObject), "BulletPush")]
    internal static class NpoBulletPushSkipProjectilesPatch
    {
        private static bool Prefix(NetworkPhysicsObject __instance, Vector2 force)
        {
            return ImpulseFilter.Allow("npo.bulletpush", __instance != null ? __instance.transform : null, force);
        }
    }

    [HarmonyPatch]
    internal static class NpoSendForceSkipProjectilesPatch
    {
        private static bool Prepare() => TargetMethod() != null;

        private static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(NetworkPhysicsObject), "RPCA_SendForce");

        private static bool Prefix(NetworkPhysicsObject __instance, Vector2 forceSent)
        {
            return ImpulseFilter.Allow("npo.sendforce", __instance != null ? __instance.transform : null, forceSent);
        }
    }

    [HarmonyPatch(typeof(Rigidbody2D), nameof(Rigidbody2D.AddForce), typeof(Vector2), typeof(ForceMode2D))]
    internal static class RbImpulseSkipProjectilesPatch
    {
        private static bool Prefix(Rigidbody2D __instance, Vector2 force, ForceMode2D mode)
        {
            if (mode != ForceMode2D.Impulse) return true;
            return ImpulseFilter.Allow("rb.addforce", __instance != null ? __instance.transform : null, force);
        }
    }

    [HarmonyPatch(typeof(Rigidbody2D), nameof(Rigidbody2D.AddForceAtPosition), typeof(Vector2), typeof(Vector2), typeof(ForceMode2D))]
    internal static class RbImpulseAtPositionSkipProjectilesPatch
    {
        private static bool Prefix(Rigidbody2D __instance, Vector2 force, ForceMode2D mode)
        {
            if (mode != ForceMode2D.Impulse) return true;
            return ImpulseFilter.Allow("rb.addforceatpos", __instance != null ? __instance.transform : null, force);
        }
    }
}
