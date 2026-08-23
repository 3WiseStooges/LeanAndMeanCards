using System;
using System.Reflection;
using HarmonyLib;
using LeanAndMeanCards.Cards;
using LeanAndMeanCards.Utils;
using CardsApi = ModdingUtils.Utils.Cards;

namespace LeanAndMeanCards.Patches
{
    [HarmonyPatch]
    internal static class JarOfDirtOfferPatch
    {
        private static bool Prepare() => TargetMethod() != null;

        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(CardsApi), "PlayerIsAllowedCard", new[] { typeof(Player), typeof(CardInfo) })
                   ?? AccessTools.Method(typeof(CardsApi), "PlayerIsAllowedCard");
        }

        private static void Postfix(Player player, CardInfo card, ref bool __result)
        {
            try
            {
                if (!__result || player == null || card == null) return;
                if (IsJar(card))
                {
                    __result = JarOfDirtManager.HasEligibleNulls(OfferPlayer(player));
                }
            }
            catch (Exception ex)
            {
                // Leave __result unchanged — never abort hand build / ReplaceCards online.
                Plugin.Instance?.LogWarn($"JarOfDirtOfferPatch skipped: {ex.Message}");
            }
        }

        private static Player OfferPlayer(Player player)
        {
            if (CardChoice.instance != null && CardChoice.instance.IsPicking)
            {
                var picker = PickPhase.GetCurrentPicker();
                if (picker != null) return picker;
            }

            return player;
        }

        private static bool IsJar(CardInfo card)
        {
            if (JarOfDirt.Card != null && card == JarOfDirt.Card) return true;
            return string.Equals(card.cardName, JarOfDirt.Title, System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
