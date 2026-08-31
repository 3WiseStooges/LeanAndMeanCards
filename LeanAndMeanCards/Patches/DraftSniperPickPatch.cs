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
            // Only the first is a selection.
            if (pickedCard == null) return true;

            if (ExternalPickState.IsBulkCollecting) return true;
            if (!DraftSniperManager.IsBlocked(pickedCard))
            {
                // A real pick is about to consume pickrID. Stop restoring it or the
                // picker could confirm a second card during IDoEndPick.
                DraftSniperManager.ClearPickerHold();
                return true;
            }

            DraftSniperManager.NoteBlockedPick(CardChoice.instance);
            DraftSniperManager.NotifyLockedClick();
            return false;
        }
    }
}
