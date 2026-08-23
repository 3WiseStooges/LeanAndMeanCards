using System;
using HarmonyLib;
using LeanAndMeanCards.Cards;
using LeanAndMeanCards.Utils;
using UnityEngine;

namespace LeanAndMeanCards.Patches
{
    /// <summary>
    /// Marks the window in which a map-edge bounce is being processed, so Safety Net can
    /// suppress only that damage and Bozo Shoes can amplify only that knockback.
    ///
    /// Vanilla OutOfBoundsHandler.LateUpdate only calls CallTakeForce / CallTakeDamage when
    /// data.view.IsMine, so the flag is set on the owning client alone. Setting it on every
    /// client would widen the window for no benefit. The Finalizer guarantees the flag is
    /// cleared even if the LateUpdate body throws — a stale flag would suppress unrelated
    /// damage for the rest of the match.
    /// </summary>
    [HarmonyPatch(typeof(OutOfBoundsHandler), "LateUpdate")]
    internal static class MapEdgeOobFlagPatch
    {
        internal static Player Current;

        private static void Prefix(OutOfBoundsHandler __instance)
        {
            Current = null;
            try
            {
                var tr = Traverse.Create(__instance);
                if (!IsOobFlag(tr.Field("outOfBounds").GetValue()) &&
                    !IsOobFlag(tr.Field("almostOutOfBounds").GetValue()))
                {
                    return;
                }

                var data = tr.Field("data").GetValue<CharacterData>();
                var player = data != null ? data.player : null;

                // Only the owning client turns edge handling into damage / force, so only the
                // owner should evaluate these card effects. Anything else risks one client
                // cancelling damage the others applied.
                var view = data?.view ?? player?.GetComponent<Photon.Pun.PhotonView>();
                if (view == null || !view.IsMine) return;

                Current = player;
            }
            catch
            {
                Current = null;
            }
        }

        private static bool IsOobFlag(object value) => value is bool b && b;

        private static void Postfix() => Current = null;

        private static Exception Finalizer(Exception __exception)
        {
            Current = null;
            return __exception;
        }
    }

    /// <summary>
    /// Safety Net: map edges stop dealing damage.
    ///
    /// Patches CallTakeDamage only — that is where the damage RPC is raised
    /// (view.RPC("RPCA_SendTakeDamage", RpcTarget.All, ...)), so cancelling it here suppresses
    /// the hit for every client at once. An earlier version also prefixed the private DoDamage,
    /// which runs on *every* client from that RPC; combined with an owner-scoped flag that
    /// would have cancelled damage on some clients but not others, desyncing health.
    /// </summary>
    [HarmonyPatch(typeof(HealthHandler), nameof(HealthHandler.CallTakeDamage))]
    internal static class MapEdgeDamagePatch
    {
        private static bool Prefix(HealthHandler __instance)
        {
            var player = MapEdgeOobFlagPatch.Current;
            if (player == null) return true;

            var victim = __instance.GetComponentInParent<Player>();
            if (victim != player) return true;

            return !CardOwnership.Has(player, SafetyNet.Card);
        }
    }

    /// <summary>
    /// Bozo Shoes: marked players take extra knockback.
    ///
    /// CallTakeForce is the broadcast point, so the multiplier is baked into the value every
    /// client receives. Whoever calls it is the single authority for that force event.
    /// </summary>
    [HarmonyPatch(typeof(HealthHandler), nameof(HealthHandler.CallTakeForce))]
    internal static class KnockbackForcePatch
    {
        private static void Prefix(HealthHandler __instance, ref Vector2 force, bool forceIgnoreMass)
        {
            var victim = __instance.GetComponentInParent<Player>();
            if (victim == null) return;
            if (!BozoShoesRuntime.IsMarked(victim)) return;

            // Self-kicks (Yeet Cannon) use forceIgnoreMass. Leave those alone unless this is
            // actually a map-edge bounce.
            if (forceIgnoreMass && MapEdgeOobFlagPatch.Current != victim) return;

            force *= BozoShoes.KnockbackMultiplier;
        }
    }
}
