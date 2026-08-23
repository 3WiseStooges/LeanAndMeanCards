using System;
using System.Reflection;
using HarmonyLib;
using CardsApi = ModdingUtils.Utils.Cards;

namespace LeanAndMeanCards.Patches
{
    /// <summary>
    /// PPI ReplaceCards builds ValidCards via PlayerIsAllowedCard. ModdingUtils catches
    /// validation-function throws, but null currentCards / categories inside allow-card
    /// itself can still abort the coroutine → empty online offer. Never throw out.
    /// </summary>
    [HarmonyPatch]
    internal static class PlayerIsAllowedCardSafetyPatch
    {
        private static bool Prepare() => TargetMethod() != null;

        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(CardsApi), "PlayerIsAllowedCard", new[] { typeof(Player), typeof(CardInfo) })
                   ?? AccessTools.Method(typeof(CardsApi), "PlayerIsAllowedCard");
        }

        private static Exception Finalizer(Exception __exception, ref bool __result)
        {
            if (__exception == null) return null;
            Plugin.Instance?.LogWarn(
                $"PlayerIsAllowedCard threw {__exception.GetType().Name}: {__exception.Message} (allowing card)");
            __result = true;
            return null;
        }
    }
}
