using System;
using System.Reflection;
using HarmonyLib;
using LeanAndMeanCards.Cards;
using UnityEngine;

namespace LeanAndMeanCards.Utils
{
    internal static class TakebacksiesInjector
    {
        private static bool _registered;

        internal static void Register()
        {
            if (_registered) return;

            try
            {
                var type = AccessTools.TypeByName("PickPhaseImprovements.PickManager");
                var method = type == null
                    ? null
                    : AccessTools.Method(type, "RegisterHandModificationFunction", new[] { typeof(Func<CardInfo[], CardInfo[]>), typeof(int) });
                if (method == null) return;

                method.Invoke(null, new object[] { (Func<CardInfo[], CardInfo[]>)InjectTakebacksies, 0 });
                _registered = true;
                Plugin.Instance.Log("Registered Takebacksies hand injector.");
            }
            catch (Exception ex)
            {
                Plugin.Instance.LogWarn($"Takebacksies injector unavailable: {ex.Message}");
            }
        }

        private static CardInfo[] InjectTakebacksies(CardInfo[] hand)
        {
            try
            {
                if (hand == null || hand.Length == 0) return hand;
                if (Takebacksies.Card == null || !CardPool.IsActive(Takebacksies.Card)) return hand;

                var picker = PickPhase.GetCurrentPicker();
                if (picker == null || !StealLedger.HasPendingTakeback(picker.playerID)) return hand;

                foreach (var card in hand)
                {
                    if (card == Takebacksies.Card) return hand;
                }

                var result = new CardInfo[hand.Length];
                Array.Copy(hand, 0, result, 1, hand.Length - 1);
                result[0] = Takebacksies.Card;
                return result;
            }
            catch (Exception ex)
            {
                // Never abort Pick Phase Improvements' ReplaceCards coroutine online.
                Plugin.Instance?.LogWarn($"Takebacksies inject skipped: {ex.Message}");
                return hand;
            }
        }
    }
}
