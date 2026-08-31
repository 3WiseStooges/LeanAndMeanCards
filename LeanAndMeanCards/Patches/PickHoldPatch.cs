using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using LeanAndMeanCards.Utils;
using UnityEngine;

namespace LeanAndMeanCards.Patches
{
    /// <summary>
    /// Delays the end-of-pick animation while a card is still showing its own UI, then pumps
    /// the original coroutine.
    ///
    /// Pumping someone else's iterator by hand makes this the single point where every mod's
    /// IDoEndPick work can fail, so both loops here are bounded and guarded. IDoEndPick is
    /// the coroutine that deals the next hand:
    ///
    ///     spawnedCards.Clear();
    ///     if (PlayerManager.instance.GetPlayerWithID(pickId).data.view.IsMine)
    ///         StartCoroutine(ReplaceCards(pickedCard));
    ///
    /// A throw anywhere above that, or a hold that is never released, means the line never
    /// runs and nobody is offered another card - the whole lobby sits watching an empty
    /// table. That is not a failure worth propagating faithfully, so a broken pump finishes
    /// the handoff itself.
    /// </summary>
    [HarmonyPatch(typeof(CardChoice), "IDoEndPick")]
    internal static class HoldEndPickPatch
    {
        // Long enough that no legitimate card UI is cut short, short enough that a dropped
        // PickUiHold.Pop RPC costs a pause instead of the match.
        private const float MaxHoldSeconds = 8f;

        private static void Postfix(CardChoice __instance, GameObject pickedCard, int pickId, ref IEnumerator __result)
        {
            __result = WaitThen(__result, __instance, pickedCard, pickId);
        }

        private static IEnumerator WaitThen(IEnumerator original, CardChoice choice, GameObject pickedCard, int pickId)
        {
            var waitUntil = Time.unscaledTime + MaxHoldSeconds;
            while (PickUiHold.ShouldWait)
            {
                if (Time.unscaledTime > waitUntil)
                {
                    Plugin.Instance?.LogWarn(
                        $"Pick hold exceeded {MaxHoldSeconds:0}s and was released - a card UI never popped its hold.");
                    PickUiHold.Reset();
                    break;
                }

                yield return null;
            }

            if (original == null) yield break;

            while (true)
            {
                object current;

                // MoveNext runs the foreign coroutine body. The yield sits outside the try
                // because C# forbids yielding from a try that has a catch.
                try
                {
                    if (!original.MoveNext()) yield break;
                    current = original.Current;
                }
                catch (Exception ex)
                {
                    Plugin.Instance?.LogWarn(
                        $"IDoEndPick threw {ex.GetType().Name}: {ex.Message} - completing the pick without it.");
                    FinishPick(choice, pickedCard, pickId);
                    yield break;
                }

                yield return current;
            }
        }

        /// <summary>
        /// Does what the tail of vanilla IDoEndPick would have done, for a run that died
        /// before reaching it.
        ///
        /// The isPlaying check is the interlock against dealing two hands: ReplaceCards sets
        /// it synchronously, and StartCoroutine runs a body up to its first yield, so a
        /// handoff another patch already made is visible here by the time we look.
        /// </summary>
        private static void FinishPick(CardChoice choice, GameObject pickedCard, int pickId)
        {
            try
            {
                if (choice == null || !choice.IsPicking) return;

                var isPlayingField = AccessTools.Field(typeof(CardChoice), "isPlaying");
                if (isPlayingField != null && (bool)isPlayingField.GetValue(choice)) return;

                var spawned = AccessTools.Field(typeof(CardChoice), "spawnedCards")
                    ?.GetValue(choice) as List<GameObject>;
                spawned?.Clear();

                var picker = PickPhase.FindPlayer(pickId);
                if (picker?.data?.view == null || !picker.data.view.IsMine) return;

                var replace = AccessTools.Method(
                    typeof(CardChoice), "ReplaceCards", new[] { typeof(GameObject), typeof(bool) });
                if (replace == null) return;

                choice.StartCoroutine((IEnumerator)replace.Invoke(choice, new object[] { pickedCard, false }));
                Plugin.Instance?.Log($"Recovered the pick phase for player {pickId} after a failed IDoEndPick.");
            }
            catch (Exception ex)
            {
                Plugin.Instance?.LogWarn($"Pick recovery failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
