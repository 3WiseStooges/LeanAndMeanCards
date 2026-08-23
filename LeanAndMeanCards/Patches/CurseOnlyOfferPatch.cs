using System;
using System.Reflection;
using HarmonyLib;
using LeanAndMeanCards.Utils;
using CardsApi = ModdingUtils.Utils.Cards;

namespace LeanAndMeanCards.Patches
{
    /// <summary>
    /// Makes every card offered to a configured Steam account a curse.
    ///
    /// Priority.Last so this is the final word on the offer, after Jar of Dirt and
    /// anything else that narrows the pool.
    /// </summary>
    [HarmonyPatch]
    internal static class CurseOnlyOfferPatch
    {
        private static bool Prepare() => TargetMethod() != null;

        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(CardsApi), "PlayerIsAllowedCard", new[] { typeof(Player), typeof(CardInfo) })
                   ?? AccessTools.Method(typeof(CardsApi), "PlayerIsAllowedCard");
        }

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Player player, CardInfo card, ref bool __result)
        {
            // WWM's PlayerIsAllowedCurse calls straight back into PlayerIsAllowedCard.
            // Leave the inner call alone or it recurses forever.
            if (CurseOnlyPlayers.Reentrant) return;
            if (player == null || card == null) return;

            try
            {
                if (!CurseOnlyPlayers.AppliesTo(player)) return;

                CurseOnlyPlayers.Reentrant = true;
                try
                {
                    if (!CurseOnlyPlayers.CanRestrict(player)) return;

                    // Ask WWM rather than forcing true: a curse the player already holds,
                    // or one their current cards blacklist, must still be rejected.
                    __result = WwmCurses.IsCurse(card) && WwmCurses.IsDrawableCurse(player, card);
                }
                finally
                {
                    CurseOnlyPlayers.Reentrant = false;
                }
            }
            catch (Exception ex)
            {
                // Leave __result untouched. Throwing here aborts ReplaceCards and empties
                // the online offer entirely.
                CurseOnlyPlayers.Reentrant = false;
                Plugin.Instance?.LogWarn($"CurseOnlyOfferPatch skipped: {ex.Message}");
            }
        }
    }
}
