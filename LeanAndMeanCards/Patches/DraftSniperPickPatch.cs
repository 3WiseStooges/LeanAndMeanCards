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
        private static int _cancelledPickerId = -1;

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

            _cancelledPickerId = CardChoice.instance != null ? CardChoice.instance.pickrID : -1;
            DraftSniperManager.NotifyLockedClick();
            return false;
        }

        /// <summary>
        /// Hands the pick phase back after a cancelled selection.
        ///
        /// DoPlayerSelect does not check whether Pick did anything — it runs
        /// `Pick(card); pickrID = -1; break;`. Cancelling Pick therefore still retired the
        /// picker, and CardChoice.Update stops calling DoPlayerSelect once pickrID is -1,
        /// so the picker was left staring at a hand they could no longer select from. A
        /// lock is meant to remove one card, not end someone's turn, so the id the cancel
        /// cost them goes straight back.
        /// </summary>
        internal static void RestorePickerAfterCancel(CardChoice choice)
        {
            if (_cancelledPickerId < 0 || choice == null) return;
            var pickerId = _cancelledPickerId;
            _cancelledPickerId = -1;
            if (choice.IsPicking && choice.pickrID == -1) choice.pickrID = pickerId;
        }

        internal static void ForgetCancel() => _cancelledPickerId = -1;
    }

    [HarmonyPatch(typeof(CardChoice), "DoPlayerSelect")]
    internal static class DraftSniperKeepPickerPatch
    {
        // Armed for the duration of this call only. Other mods call CardChoice.Pick
        // directly and do not retire the picker afterwards, so a flag left over from one of
        // those must never resurrect a pickrID that vanilla cleared for its own reasons.
        private static void Prefix() => DraftSniperPickPatch.ForgetCancel();

        private static void Postfix(CardChoice __instance)
        {
            DraftSniperPickPatch.RestorePickerAfterCancel(__instance);
        }
    }
}
