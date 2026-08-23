using HarmonyLib;
using LeanAndMeanCards.Utils;
using UnityEngine;

namespace LeanAndMeanCards.Patches
{
    /// <summary>
    /// Stops a picker taking a card Draft Sniper has locked.
    /// </summary>
    [HarmonyPatch(typeof(CardChoice), nameof(CardChoice.Pick))]
    [HarmonyPriority(Priority.Last)]
    internal static class DraftSniperPickPatch
    {
        private static bool Prefix(GameObject pickedCard)
        {
            // CardChoice.Pick has two jobs depending on its argument:
            //   Pick(card) — the picker confirmed that card.
            //   Pick(null) — StartPick asking for a fresh offer to be built.
            // Only the first is a selection. Cancelling the second means ReplaceCards
            // never runs and the pick phase comes up with no cards at all, which is
            // exactly what happened when IsBlocked(null) returned true here.
            if (pickedCard == null) return true;

            if (ExternalPickState.IsBulkCollecting) return true;
            if (!DraftSniperManager.IsBlocked(pickedCard)) return true;

            DraftSniperManager.NotifyLockedClick();
            return false;
        }
    }
}
