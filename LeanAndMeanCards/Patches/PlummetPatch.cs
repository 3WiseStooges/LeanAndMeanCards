using System;
using System.Collections.Generic;
using HarmonyLib;
using LeanAndMeanCards.Cards;
using LeanAndMeanCards.Utils;
using Photon.Pun;
using UnboundLib;
using UnboundLib.Networking;
using UnityEngine;

namespace LeanAndMeanCards.Patches
{
    /// <summary>
    /// Plummet swaps a bullet's knockback for a straight-down dive.
    ///
    /// The swap happens where vanilla decides knockback: ProjectileHit.RPCA_DoHit calls
    /// HealthHandler.CallTakeForce, but only on the client with hasControl — the shooter's,
    /// or the host for a bot — and CallTakeForce is what broadcasts the push to everyone.
    /// Taking over that one call keeps vanilla's authority model exactly: one client decides,
    /// every client applies. It also holds for the bullets whose RPCA_DoHit never leaves the
    /// shooter's machine (sendCollisions off while drilling), which a postfix trusting
    /// RPCA_DoHit to run everywhere would miss.
    ///
    /// Only the push aimed at the player the bullet hit is replaced, and only once per hit.
    /// Hit effects that run inside RPCA_DoHit and push someone else keep their own force.
    /// </summary>
    [HarmonyPatch(typeof(ProjectileHit), "RPCA_DoHit")]
    internal static class PlummetHitContextPatch
    {
        private static readonly AccessTools.FieldRef<ProjectileHit, bool> HasControlRef =
            AccessTools.FieldRefAccess<ProjectileHit, bool>("hasControl");

        // RPCA_DoHit can re-enter through hit effects, so contexts nest instead of overwrite.
        private static readonly Stack<PlummetHitContext> Saved = new Stack<PlummetHitContext>();

        internal static PlummetHitContext Current;

        private static void Prefix(ProjectileHit __instance, int viewID, bool wasBlocked)
        {
            Saved.Push(Current);
            Current = null;
            try
            {
                Current = Create(__instance, viewID, wasBlocked);
            }
            catch
            {
                Current = null;
            }
        }

        private static Exception Finalizer(Exception __exception)
        {
            Current = Saved.Count > 0 ? Saved.Pop() : null;
            return __exception;
        }

        private static PlummetHitContext Create(ProjectileHit hit, int viewID, bool wasBlocked)
        {
            if (hit == null || wasBlocked || viewID <= 0) return null;
            if (!HasControlRef(hit)) return null;

            var shooter = hit.ownPlayer;
            if (shooter == null || !CardOwnership.Has(shooter, Plummet.Card)) return null;

            // Same lookup RPCA_DoHit does: hitInfo.transform is the view's transform and the
            // pushed HealthHandler is the one sitting on it.
            var view = PhotonNetwork.GetPhotonView(viewID);
            var target = view != null ? view.transform.GetComponent<HealthHandler>() : null;
            if (target == null) return null;

            return new PlummetHitContext(shooter, target);
        }
    }

    internal sealed class PlummetHitContext
    {
        internal readonly Player Shooter;
        internal readonly HealthHandler Target;
        internal bool Consumed;

        internal PlummetHitContext(Player shooter, HealthHandler target)
        {
            Shooter = shooter;
            Target = target;
        }
    }

    [HarmonyPatch(typeof(HealthHandler), nameof(HealthHandler.CallTakeForce))]
    internal static class PlummetKnockbackPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(HealthHandler __instance)
        {
            var ctx = PlummetHitContextPatch.Current;
            if (ctx == null || ctx.Consumed || ctx.Target != __instance) return true;
            ctx.Consumed = true;

            try
            {
                return !TryBroadcast(ctx.Shooter, __instance);
            }
            catch (Exception ex)
            {
                Plugin.Instance?.LogWarn($"Plummet skipped, vanilla knockback kept: {ex.Message}");
                return true;
            }
        }

        /// <summary>
        /// True when the dive was sent and vanilla's push must not also fire. Anything that
        /// makes vanilla's CallTakeForce a no-op (blocking, not playing) is left to vanilla.
        /// </summary>
        private static bool TryBroadcast(Player shooter, HealthHandler health)
        {
            var data = health.GetComponent<CharacterData>();
            var victim = data != null ? data.player : null;
            if (victim == null || shooter == null) return false;

            // Your own reflected or homing shot pushes you the normal way.
            if (victim.playerID == shooter.playerID) return false;
            if (!data.isPlaying || data.dead) return false;
            if (data.block != null && data.block.IsBlocking()) return false;

            var copies = CardOwnership.CountOf(shooter, Plummet.Card);
            var speed = PlummetRuntime.SpeedFor(copies);
            if (speed <= 0f) return false;

            // Bozo Shoes is "+50% knockback from everyone"; the dive is this card's knockback.
            if (BozoShoesRuntime.IsMarked(victim)) speed *= BozoShoes.KnockbackMultiplier;

            NetworkingManager.RPC(typeof(PlummetKnockbackPatch), nameof(RPCA_Plummet),
                victim.playerID, Mathf.Min(speed, PlummetRuntime.MaxSpeed), copies);
            return true;
        }

        [UnboundRPC]
        public static void RPCA_Plummet(int victimId, float speed, int copies)
        {
            PlummetRuntime.Begin(PickPhase.FindPlayer(victimId), speed, copies);
        }
    }

    /// <summary>
    /// Ends a dive the moment its victim crosses any map edge, before vanilla's edge
    /// handling runs. Every client, because every client is holding the dive.
    /// </summary>
    [HarmonyPatch(typeof(OutOfBoundsHandler), "LateUpdate")]
    internal static class PlummetEdgePatch
    {
        private static readonly AccessTools.FieldRef<OutOfBoundsHandler, CharacterData> DataRef =
            AccessTools.FieldRefAccess<OutOfBoundsHandler, CharacterData>("data");

        [HarmonyPriority(Priority.First)]
        private static void Prefix(OutOfBoundsHandler __instance)
        {
            try
            {
                PlummetRuntime.CheckEdge(DataRef(__instance));
            }
            catch
            {
            }
        }
    }
}
