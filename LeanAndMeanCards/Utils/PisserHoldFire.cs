using HarmonyLib;
using LeanAndMeanCards.Cards;
using UnityEngine;

namespace LeanAndMeanCards.Utils
{
    /// <summary>
    /// Pisser is a hold-to-spray gun. Spray (and similar cards) can set
    /// dontAllowAutoFire / useCharge after Pisser's OnAddCard, which forces click-to-fire.
    /// Keep full-auto as long as Pisser is on the player.
    /// </summary>
    internal static class PisserHoldFire
    {
        internal static void Ensure(Player player)
        {
            if (player == null) return;
            if (player.GetComponent<PisserHoldFireTicker>() == null)
                player.gameObject.AddComponent<PisserHoldFireTicker>();
            Enforce(player);
        }

        internal static void Enforce(Player player)
        {
            if (player == null || !CardOwnership.Has(player, Pisser.Card)) return;
            EnforceGun(GunOf(player));
        }

        internal static void EnforceGun(Gun gun)
        {
            if (gun == null) return;
            var player = PlayerOf(gun);
            if (player == null || !CardOwnership.Has(player, Pisser.Card)) return;
            gun.dontAllowAutoFire = false;
            gun.useCharge = false;
        }

        private static Gun GunOf(Player player)
        {
            if (player?.data?.weaponHandler != null && player.data.weaponHandler.gun != null)
                return player.data.weaponHandler.gun;
            return player != null ? player.GetComponentInChildren<Gun>(true) : null;
        }

        private static Player PlayerOf(Gun gun)
        {
            try
            {
                var field = AccessTools.Field(typeof(Gun), "player");
                if (field?.GetValue(gun) is Player p) return p;
            }
            catch
            {
            }

            return gun != null ? gun.GetComponentInParent<Player>() : null;
        }
    }

    internal sealed class PisserHoldFireTicker : MonoBehaviour
    {
        private Player _player;

        private void Awake() => _player = GetComponent<Player>();

        private void LateUpdate()
        {
            if (_player == null) return;
            if (!CardOwnership.Has(_player, Pisser.Card))
            {
                Destroy(this);
                return;
            }

            PisserHoldFire.Enforce(_player);
        }
    }

    [HarmonyPatch(typeof(Gun), "Attack")]
    internal static class PisserAttackPatch
    {
        private static void Prefix(Gun __instance) => PisserHoldFire.EnforceGun(__instance);
    }
}
