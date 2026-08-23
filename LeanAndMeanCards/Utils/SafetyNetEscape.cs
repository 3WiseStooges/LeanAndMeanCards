using System.Collections.Generic;
using HarmonyLib;
using LeanAndMeanCards.Cards;
using UnityEngine;

namespace LeanAndMeanCards.Utils
{
    /// <summary>
    /// Safety Net stops edge damage, which can soft-lock players under/outside the map.
    /// After staying out of bounds long enough, force a kill so the point can continue.
    /// </summary>
    internal static class SafetyNetEscape
    {
        private const float KillAfterSeconds = 2.75f;
        private static readonly Dictionary<int, float> OobSeconds = new Dictionary<int, float>();

        internal static void Reset() => OobSeconds.Clear();

        internal static void Tick(Player player, bool outOfBounds, float dt)
        {
            if (player == null) return;
            var id = player.playerID;

            if (!outOfBounds || !CardOwnership.Has(player, SafetyNet.Card))
            {
                OobSeconds.Remove(id);
                return;
            }

            if (player.data?.view != null && !player.data.view.IsMine) return;
            if (player.data?.dead == true) return;

            OobSeconds.TryGetValue(id, out var accrued);
            accrued += dt;
            OobSeconds[id] = accrued;
            if (accrued < KillAfterSeconds) return;

            OobSeconds.Remove(id);
            ForceKill(player);
        }

        private static void ForceKill(Player player)
        {
            var health = player?.data?.healthHandler;
            if (health == null) return;

            try
            {
                // Massive OOB-style damage; ignore Safety Net by clearing the OOB flag context.
                Patches.MapEdgeOobFlagPatch.Current = null;
                health.CallTakeDamage(
                    Vector2.up * 9999f,
                    (Vector2)player.transform.position,
                    null,
                    null,
                    true);
            }
            catch
            {
                try
                {
                    AccessTools.Method(typeof(HealthHandler), "DoDamage", new[]
                    {
                        typeof(Vector2), typeof(Vector2), typeof(Color), typeof(GameObject),
                        typeof(Player), typeof(bool), typeof(bool), typeof(bool)
                    })?.Invoke(health, new object[]
                    {
                        Vector2.up * 9999f,
                        (Vector2)player.transform.position,
                        Color.white,
                        null,
                        null,
                        true,
                        false,
                        true
                    });
                }
                catch
                {
                    // Last resort: mark dead if the API shape differs.
                    if (player.data != null) player.data.dead = true;
                }
            }
        }
    }

    [HarmonyPatch(typeof(OutOfBoundsHandler), "LateUpdate")]
    internal static class SafetyNetEscapePatch
    {
        private static void Postfix(OutOfBoundsHandler __instance)
        {
            var tr = Traverse.Create(__instance);
            var data = tr.Field("data").GetValue<CharacterData>();
            var player = data != null ? data.player : null;
            var oob = tr.Field("outOfBounds").GetValue() is bool b && b;
            SafetyNetEscape.Tick(player, oob, Time.deltaTime);
        }
    }
}
