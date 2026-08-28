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

        /// <summary>
        /// Covers hits that never produce a ProjectileHit — explosions, Dynamite, map
        /// hazards.
        ///
        /// healthRemoval is the game's own marker for a damage-over-time tick:
        /// DamageOverTime.DoDamageOverTime is the only caller that passes true, and it calls
        /// DoDamage once per interval with the *shooter* still set as damagingPlayer.
        /// Without this check one POISON or Infernal bullet re-upped the taser stun every
        /// 0.25s for the whole burn.
        /// </summary>
        private static void Postfix(HealthHandler __instance, Player damagingPlayer, bool healthRemoval)
        {
            if (healthRemoval) return;
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

        /// <summary>
        /// Applies the taser stun locally. Never broadcasts.
        ///
        /// Both call sites already run on every client for the same event — DoDamage arrives
        /// through RPCA_SendTakeDamage (RpcTarget.All) and RPCA_DoHit is itself an
        /// RpcTarget.All call — so the extra RPC only added a second, differently-timed
        /// application of a stun that was already replicated.
        /// </summary>
        internal static bool TryTaserStun(HealthHandler health, Player damagingPlayer)
        {
            if (damagingPlayer == null || health == null) return false;
            var victim = health.GetComponentInParent<Player>();
            if (victim == null) return false;
            if (!CardOwnership.Has(damagingPlayer, TaserTaserTaser.Card)) return false;
            return ApplyStun(victim);
        }

        /// <summary>
        /// Exactly one client may broadcast a combat effect.
        ///
        /// HealthHandler.RPCA_SendTakeDamage runs DoDamage on every client, so this prefix
        /// fires everywhere. Returning true for "master OR shooter" meant a non-host shooter
        /// and the host both broadcast the same effect: two RPCs on top of each client's own
        /// local application. The master is the single authority.
        /// </summary>
        private static bool ShouldBroadcastCombat(Player damagingPlayer)
        {
            return PhotonNetwork.OfflineMode || PhotonNetwork.IsMasterClient;
        }

        /// <summary>
        /// One stun per hit, through the game's own entry point.
        ///
        /// StunHandler.AddStun raises data.stunTime and starts the stun itself, but only
        /// when the victim is not blocking. The old code wrote data.stunTime directly and
        /// then reflected into the private StartStun(), which skipped that block check,
        /// zeroed the victim's velocity, and played the stun animation even on frames where
        /// AddStun had declined. That forced animation is the "tazed without being shot"
        /// flash.
        /// </summary>
        internal static bool ApplyStun(Player victim)
        {
            if (victim?.data == null) return false;
            if (victim.data.dead || !victim.data.isPlaying) return false;
            if (TaserStunGate.WasRecent(victim.playerID)) return false;

            var stun = victim.data.stunHandler ?? victim.GetComponentInChildren<StunHandler>(true);
            if (stun == null) return false;

            TaserStunGate.Mark(victim.playerID);
            stun.AddStun(TaserTaserTaser.ExtraStunSeconds);

            TaserStunGate.Log(victim.playerID);
            return true;
        }

        [UnboundRPC]
        public static void RPCA_BozoMark(int victimId)
        {
            BozoShoesRuntime.Mark(PickPhase.FindPlayer(victimId));
        }
    }

    /// <summary>
    /// "Once per hit" in practice.
    ///
    /// The window is the stun's own length: inside it the victim is already tazed, so a
    /// second application could only extend a stun that is still running; outside it the
    /// stun has expired and a fresh hit deserves a fresh one. The old 0.2s window was
    /// shorter than the 0.5s stun, which is why a burst kept topping it back up.
    /// </summary>
    internal static class TaserStunGate
    {
        private static readonly Dictionary<int, float> Times = new Dictionary<int, float>();
        private static float _lastLog;

        internal static bool WasRecent(int playerId) =>
            Times.TryGetValue(playerId, out var t) && Time.time - t < TaserTaserTaser.ExtraStunSeconds;

        internal static void Mark(int playerId) => Times[playerId] = Time.time;

        // 219 identical lines in one match is not a log, it is noise.
        internal static void Log(int playerId)
        {
            if (Time.time - _lastLog < 1f) return;
            _lastLog = Time.time;
            Plugin.Instance?.Log($"TASER stun player={playerId}");
        }
    }

    // Backup path for Bozo only. The taser deliberately does not hook here: CallTakeDamage
    // is the *broadcast* point, called once on one client, and the DoDamage it produces on
    // every client already carries the same hit.
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
    }

    [HarmonyPatch(typeof(ProjectileHit), "Hit")]
    internal static class BozoProjectileHitPatch
    {
        /// <summary>
        /// Bozo's mark only.
        ///
        /// ProjectileHit.Hit is not a confirmed hit: RayCastTrail.Update runs on every
        /// client and calls it from that client's own prediction, it runs before the
        /// blocked / already-hit checks decide anything, and only the bullet's owner turns
        /// it into an RPC. Stunning from here tazed players the authoritative sim never hit
        /// — and on the host it then broadcast that phantom stun to everyone. RPCA_DoHit
        /// below is the confirmed version.
        /// </summary>
        private static void Postfix(ProjectileHit __instance, HitInfo hit)
        {
            if (__instance?.ownPlayer == null || hit?.transform == null) return;
            var health = hit.transform.GetComponentInParent<HealthHandler>()
                         ?? hit.transform.GetComponentInChildren<HealthHandler>();
            CombatEffectPatch.TryBozoMark(health, __instance.ownPlayer);
        }
    }

    /// <summary>
    /// RPCA_DoHit runs on every client (the same hook Dynamite plants from) and carries the
    /// hit the shooter actually confirmed, so Bozo / TASER apply here for a non-host's shots
    /// too — and only for hits that were not blocked.
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
                CombatEffectPatch.TryTaserStun(health, __instance.ownPlayer);
            }
            catch
            {
            }
        }
    }
}
