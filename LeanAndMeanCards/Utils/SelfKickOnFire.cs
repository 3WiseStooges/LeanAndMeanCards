using HarmonyLib;
using LeanAndMeanCards.Cards;
using UnityEngine;

namespace LeanAndMeanCards.Utils
{
    /// <summary>
    /// Applies self-knockback opposite the gun aim for Yeet Cannon.
    /// Uses Gun.Attack postfix so it stays reliable when guns are replaced mid-match.
    /// </summary>
    internal static class SelfKick
    {
        internal const float YeetForce = 2400f;
        internal const float YeetFlying = 0.32f;

        private static float _lastKickTime;
        private static int _lastKickPlayer = -1;

        internal static void Ensure(Player player, float ignoredForce = 0f)
        {
            // Hook is global via Gun.Attack patch; kept for card OnAddCard call sites.
        }

        internal static void TryKick(Gun gun)
        {
            if (gun == null) return;
            var player = ResolvePlayerFromGun(gun)
                         ?? gun.GetComponentInParent<Player>();
            if (player?.data?.view == null || !player.data.view.IsMine) return;
            if (player.data.healthHandler == null) return;

            if (!CardOwnership.Has(player, YeetCannon.Card)) return;

            // Attack can fire multiple internal shots; one kick per volley.
            if (player.playerID == _lastKickPlayer && Time.time - _lastKickTime < 0.05f) return;
            _lastKickPlayer = player.playerID;
            _lastKickTime = Time.time;

            var aim = (Vector2)gun.transform.right;
            if (aim.sqrMagnitude < 0.01f)
            {
                var face = player.data?.playerVel != null
                    ? (Vector2)player.transform.right
                    : Vector2.right;
                aim = face.sqrMagnitude > 0.01f ? face : Vector2.right;
            }

            // Kick away from the muzzle — opposite aim — so aiming down can boost upward.
            var forceMag = YeetForce;
            var flying = YeetFlying;

            var force = -aim.normalized * forceMag;
            // Upward bias so horizontal shots still hop; aiming down is a real jump.
            force += Vector2.up * (forceMag * 0.12f);
            player.data.healthHandler.CallTakeForce(force, ForceMode2D.Impulse, true, true, flying);
        }

        private static Player ResolvePlayerFromGun(Gun gun)
        {
            try
            {
                var field = AccessTools.Field(typeof(Gun), "player");
                if (field?.GetValue(gun) is Player p) return p;
            }
            catch
            {
            }

            return null;
        }
    }

    [HarmonyPatch(typeof(Gun), "Attack")]
    internal static class SelfKickAttackPatch
    {
        private static void Postfix(Gun __instance)
        {
            SelfKick.TryKick(__instance);
        }
    }
}
