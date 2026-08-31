using System.Reflection;
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

    /// <summary>
    /// Vanilla DoPlayerSelect does this on confirm:
    ///   Pick(spawnedCards[currentlySelectedCard]);
    ///   pickrID = -1;
    /// Cancelling Pick (locked card) still runs pickrID = -1, and the next frames
    /// return immediately because pickrID == -1 — the whole hand is unpickable.
    /// Skip the confirm when the selected card is locked, and put pickrID back if
    /// something else already wiped it.
    ///
    /// 1.2.6 restored pickrID in a postfix and cleared that flag at the start of the
    /// next DoPlayerSelect. A lock-confirm that never went through this method (mouse
    /// pick, another mod calling Pick) still retired the picker. Skipping the jump here
    /// and restoring from Tick covers both paths.
    /// </summary>
    [HarmonyPatch(typeof(CardChoice), "DoPlayerSelect")]
    internal static class DraftSniperKeepPickerPatch
    {
        private static readonly FieldInfo PickerTypeField = AccessTools.Field(typeof(CardChoice), "pickerType");
        private static readonly FieldInfo SelectedField = AccessTools.Field(typeof(CardChoice), "currentlySelectedCard");

        private static bool Prefix(CardChoice __instance)
        {
            try
            {
                if (__instance == null || !__instance.IsPicking) return true;
                var spawned = PickPhase.GetSpawnedCards();
                if (spawned == null || spawned.Count == 0) return true;

                if (ConfirmPressed(__instance))
                {
                    var selected = SelectedCard(__instance, spawned);
                    if (selected != null && DraftSniperManager.IsBlocked(selected))
                    {
                        DraftSniperManager.NoteBlockedPick(__instance);
                        DraftSniperManager.NotifyLockedClick();
                        return false;
                    }
                }

                if (WouldAutoPickFirst(spawned, __instance))
                {
                    var first = spawned[0];
                    if (first != null && DraftSniperManager.IsBlocked(first))
                    {
                        var alt = FirstUnlocked(spawned);
                        if (alt != null)
                        {
                            DraftSniperManager.ClearPickerHold();
                            __instance.Pick(alt);
                            __instance.pickrID = -1;
                        }

                        return false;
                    }
                }
            }
            catch
            {
                // Never break the pick loop.
            }

            return true;
        }

        private static void Postfix(CardChoice __instance)
        {
            DraftSniperManager.RestoreHeldPicker();
        }

        private static GameObject SelectedCard(CardChoice choice, System.Collections.Generic.List<GameObject> spawned)
        {
            var idx = 0;
            try
            {
                if (SelectedField != null)
                    idx = (int)SelectedField.GetValue(choice);
            }
            catch
            {
                idx = 0;
            }

            if (idx < 0) idx = 0;
            if (idx >= spawned.Count) idx = spawned.Count - 1;
            return spawned[idx];
        }

        private static GameObject FirstUnlocked(System.Collections.Generic.List<GameObject> spawned)
        {
            foreach (var go in spawned)
            {
                if (go != null && !DraftSniperManager.IsBlocked(go)) return go;
            }

            return null;
        }

        private static bool WouldAutoPickFirst(System.Collections.Generic.List<GameObject> spawned, CardChoice choice)
        {
            if (spawned == null || spawned.Count == 0) return false;
            return GetActions(choice) == null;
        }

        private static bool ConfirmPressed(CardChoice choice)
        {
            var actions = GetActions(choice);
            if (actions == null) return false;
            for (var i = 0; i < actions.Length; i++)
            {
                var action = actions.GetValue(i);
                if (action == null) continue;
                try
                {
                    var jump = AccessTools.Field(action.GetType(), "Jump")?.GetValue(action);
                    if (jump == null) continue;
                    var pressed = AccessTools.Property(jump.GetType(), "WasPressed")?.GetValue(jump);
                    if (pressed is true) return true;
                }
                catch
                {
                    // input layout changed
                }
            }

            return false;
        }

        private static System.Array GetActions(CardChoice choice)
        {
            if (choice == null || choice.pickrID < 0 || PlayerManager.instance == null) return null;

            var pickerType = PickerType.Player;
            try
            {
                if (PickerTypeField != null)
                    pickerType = (PickerType)PickerTypeField.GetValue(choice);
            }
            catch
            {
                // default Player
            }

            try
            {
                var method = pickerType == PickerType.Team
                    ? AccessTools.Method(typeof(PlayerManager), "GetActionsFromTeam")
                    : AccessTools.Method(typeof(PlayerManager), "GetActionsFromPlayer");
                return method?.Invoke(PlayerManager.instance, new object[] { choice.pickrID }) as System.Array;
            }
            catch
            {
                return null;
            }
        }
    }
}
