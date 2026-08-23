using HarmonyLib;
using LeanAndMeanCards.Utils;
using UnityEngine;

namespace LeanAndMeanCards.Patches
{
    [HarmonyPatch(typeof(CardChoice), nameof(CardChoice.Pick))]
    [HarmonyPriority(Priority.Last)]
    internal static class DraftSniperPickPatch
    {
        private static bool Prefix(GameObject pickedCard)
        {
            if (ExternalPickState.IsBulkCollecting) return true;
            if (!DraftSniperManager.IsBlocked(pickedCard)) return true;
            DraftSniperManager.NotifyLockedClick();
            return false;
        }
    }
}
