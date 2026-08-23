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
    [HarmonyPatch(typeof(HealthHandler), "DoDamage")]
    internal static class CombatEffectPatch
    {
        // Mark before damage/knockback so the first Bozo hit already gets +50%.
        private static void Prefix(HealthHandler __instance, Player damagingPlayer)
        {
            TryBozoMark(__instance, damagingPlayer);
        }

        private static void Postfix(HealthHandler __instance, Player damagingPlayer)
        {
            TryTaserStun(__instance, damagingPlayer);
        }

        internal static void TryBozoMark(HealthHandler health, Player damagingPlayer, bool network = true)
        {
            if (damagingPlayer == null || health == null) return;
            var victim = health.GetComponentInParent<Player>();
            if (victim == null || victim.playerID == damagingPlayer.playerID) return;
            if (!CardOwnership.Has(damagingPlayer, BozoShoes.Card)) return;

            BozoShoesRuntime.Mark(victim);
            if (network && ShouldBroadcastCombat(damagingPlayer))
            {
                NetworkingManager.RPC(typeof(CombatEffectPatch), nameof(RPCA_BozoMark), victim.playerID);
            }
        }

        internal static bool TryTaserStun(HealthHandler health, Player damagingPlayer, bool network = true)
        {
            if (damagingPlayer == null || health == null) return false;
            var victim = health.GetComponentInParent<Player>();
            if (victim == null) return false;
            if (!CardOwnership.Has(damagingPlayer, TaserTaserTaser.Card)) return false;
            if (!ApplyStun(victim)) return false;
            if (network && ShouldBroadcastCombat(damagingPlayer))
            {
                NetworkingManager.RPC(typeof(CombatEffectPatch), nameof(RPCA_TaserStun), victim.playerID);
            }

            return true;
        }

        /// <summary>
        /// Exactly one client may broadcast a combat effect.
        ///
        /// HealthHandler.RPCA_SendTakeDamage runs DoDamage on every client, so this prefix
        /// fires everywhere. Returning true for "master OR shooter" meant a non-host shooter
        /// and the host both broadcast the same effect: two RPCs on top of each client's own
        /// local application. AddStun is additive, so a taser hit stunned for a different
        /// duration on each machine. The master is the single authority.
        /// </summary>
        private static bool ShouldBroadcastCombat(Player damagingPlayer)
        {
            return PhotonNetwork.OfflineMode || PhotonNetwork.IsMasterClient;
        }

        internal static bool ApplyStun(Player victim)
        {
            if (victim?.data == null) return false;
            if (TaserStunGate.WasRecent(victim.playerID)) return false;
            TaserStunGate.Mark(victim.playerID);

            victim.data.stunTime = Mathf.Max(victim.data.stunTime, TaserTaserTaser.ExtraStunSeconds);
            var stun = victim.data.stunHandler ?? victim.GetComponentInChildren<StunHandler>(true);
            if (stun != null)
            {
                stun.AddStun(TaserTaserTaser.ExtraStunSeconds);
                try
                {
                    AccessTools.Method(typeof(StunHandler), "StartStun")?.Invoke(stun, null);
                }
                catch
                {
                }
            }

            Plugin.Instance?.Log($"TASER stun player={victim.playerID}");
            return true;
        }

        [UnboundRPC]
        public static void RPCA_BozoMark(int victimId)
        {
            BozoShoesRuntime.Mark(PickPhase.FindPlayer(victimId));
        }

        [UnboundRPC]
        public static void RPCA_TaserStun(int victimId)
        {
            ApplyStun(PickPhase.FindPlayer(victimId));
        }
    }

    internal static class TaserStunGate
    {
        private static readonly Dictionary<int, float> Times = new Dictionary<int, float>();

        internal static bool WasRecent(int playerId) =>
            Times.TryGetValue(playerId, out var t) && Time.time - t < 0.2f;

        internal static void Mark(int playerId) => Times[playerId] = Time.time;
    }

    // Backup path — some hits skip DoDamage or arrive via RPC damage first.
    [HarmonyPatch(typeof(HealthHandler), "CallTakeDamage", new[]
    {
        typeof(Vector2), typeof(Vector2), typeof(GameObject), typeof(Player), typeof(bool)
    })]
    internal static class BozoMarkCallTakeDamagePatch
    {
        private static void Prefix(HealthHandler __instance, Player damagingPlayer)
        {
            CombatEffectPatch.TryBozoMark(__instance, damagingPlayer);
        }

        private static void Postfix(HealthHandler __instance, Player damagingPlayer)
        {
            CombatEffectPatch.TryTaserStun(__instance, damagingPlayer);
        }
    }

    // Vanilla bullets stun via ProjectileHit.stun, which is networked with the shot.
    [HarmonyPatch(typeof(Gun), "ApplyProjectileStats")]
    internal static class TaserProjectilePatch
    {
        private static void Postfix(Gun __instance, GameObject __0)
        {
            if (__0 == null || __instance?.player == null) return;
            if (!CardOwnership.Has(__instance.player, TaserTaserTaser.Card)) return;
            var hit = __0.GetComponent<ProjectileHit>() ?? __0.GetComponentInChildren<ProjectileHit>(true);
            if (hit == null) return;
            hit.stun += TaserTaserTaser.ExtraStunSeconds;
        }
    }

    [HarmonyPatch(typeof(ProjectileHit), "Hit")]
    internal static class BozoProjectileHitPatch
    {
        private static void Postfix(ProjectileHit __instance, HitInfo hit)
        {
            if (__instance?.ownPlayer == null || hit?.transform == null) return;
            var health = hit.transform.GetComponentInParent<HealthHandler>()
                         ?? hit.transform.GetComponentInChildren<HealthHandler>();
            CombatEffectPatch.TryBozoMark(health, __instance.ownPlayer);
            CombatEffectPatch.TryTaserStun(health, __instance.ownPlayer);
        }
    }

    /// <summary>
    /// RPCA_DoHit runs on every client (same hook Dynamite plants from). Apply Bozo /
    /// TASER here so a non-host's shots still mark/stun on friends' screens.
    /// </summary>
    [HarmonyPatch(typeof(ProjectileHit), "RPCA_DoHit")]
    internal static class CombatRpcHitPatch
    {
        private static void Postfix(ProjectileHit __instance, bool wasBlocked, int viewID)
        {
            try
            {
                if (wasBlocked || __instance?.ownPlayer == null || viewID <= 0) return;
                var view = PhotonNetwork.GetPhotonView(viewID);
                if (view == null) return;
            var health = view.GetComponentInChildren<HealthHandler>(true)
                             ?? view.GetComponentInParent<HealthHandler>();
                if (health == null) return;
                CombatEffectPatch.TryBozoMark(health, __instance.ownPlayer, network: false);
                CombatEffectPatch.TryTaserStun(health, __instance.ownPlayer, network: false);
            }
            catch
            {
            }
        }
    }
}
