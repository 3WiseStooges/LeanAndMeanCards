using System;
using System.Collections.Generic;
using HarmonyLib;
using ModdingUtils.Utils;
using UnboundLib;
using UnityEngine;

namespace LeanAndMeanCards.Utils
{
    /// <summary>
    /// Gets a card that was handed out between rounds onto the owner's card bar.
    ///
    /// ModdingUtils' AddCardToPlayer with addToCardBar puts the card on the bar that
    /// exists at that moment, and the top-right bar is torn down between rounds and rebuilt
    /// from scratch — so a grant that lands at round end shows up in the player's stats but
    /// never on the bar. The fix is to remember what was granted and re-assert it on every
    /// pick / point start until the bar agrees with the hand.
    ///
    /// SilverEggManager carries its own older copy of this logic. Left alone deliberately:
    /// that path is shipped and tested, and this one only has to serve the copy cards.
    /// </summary>
    internal static class CardBarDebt
    {
        // Must NOT be a readonly struct: Unity Mono lacks IsReadOnlyAttribute and Harmony
        // aborts the whole assembly's patching if it sees one.
        private sealed class Debt
        {
            public int PlayerId;
            public CardInfo Card;
        }

        private static readonly List<Debt> Pending = new List<Debt>();

        internal static void Reset() => Pending.Clear();

        internal static void Remember(Player player, CardInfo card)
        {
            if (player == null || card == null) return;
            Pending.Add(new Debt { PlayerId = player.playerID, Card = card });
        }

        /// <summary>
        /// Stamps the card onto the bar now and again over the next few frames, which is
        /// when the bar is usually rebuilt.
        /// </summary>
        internal static void StampSoon(Player player, CardInfo card)
        {
            if (player == null || card == null) return;

            void Pass()
            {
                try
                {
                    EnsureOneOnBar(player, card);
                    CardBarMiniIcons.RestampAll();
                }
                catch (Exception ex)
                {
                    Plugin.Instance?.LogWarn($"Card bar stamp skipped: {ex.Message}");
                }
            }

            Pass();
            if (Unbound.Instance == null) return;
            Unbound.Instance.ExecuteAfterFrames(2, Pass);
            Unbound.Instance.ExecuteAfterFrames(8, Pass);
            Unbound.Instance.ExecuteAfterFrames(20, Pass);
        }

        internal static void Flush()
        {
            if (Pending.Count == 0) return;

            for (var i = Pending.Count - 1; i >= 0; i--)
            {
                var debt = Pending[i];
                var player = PickPhase.FindPlayer(debt.PlayerId);
                if (player == null || debt.Card == null)
                {
                    Pending.RemoveAt(i);
                    continue;
                }

                try
                {
                    EnsureOneOnBar(player, debt.Card);
                }
                catch (Exception ex)
                {
                    Plugin.Instance?.LogWarn($"Card bar flush skipped: {ex.Message}");
                    continue;
                }

                var owned = CountOwnedNamed(player, debt.Card);
                var bar = CardBarUtils.instance?.PlayersCardBar(player.playerID);
                if (owned > 0 && bar != null && CountOnBar(bar, debt.Card) >= owned)
                {
                    Pending.RemoveAt(i);
                }
            }

            try
            {
                CardBarMiniIcons.RestampAll();
            }
            catch (Exception ex)
            {
                Plugin.Instance?.LogWarn($"Card bar restamp skipped: {ex.Message}");
            }
        }

        private static void EnsureOneOnBar(Player player, CardInfo card)
        {
            if (player == null || card == null) return;
            var owned = CountOwnedNamed(player, card);
            if (owned <= 0) return;

            var bar = CardBarUtils.instance?.PlayersCardBar(player.playerID);
            if (bar != null && CountOnBar(bar, card) >= owned) return;

            try
            {
                CardBarUtils.SilentAddToCardBar(player.playerID, card, "");
            }
            catch (Exception ex)
            {
                Plugin.Instance?.LogWarn($"SilentAddToCardBar skipped: {ex.Message}");
            }

            bar = CardBarUtils.instance?.PlayersCardBar(player.playerID);
            if (bar != null && CountOnBar(bar, card) >= owned) return;
            if (CardBarHandler.instance == null) return;

            try
            {
                CardBarHandler.instance.AddCard(player.playerID, card);
            }
            catch (Exception ex)
            {
                Plugin.Instance?.LogWarn($"CardBarHandler.AddCard skipped: {ex.Message}");
            }
        }

        private static int CountOwnedNamed(Player player, CardInfo card)
        {
            var cards = player?.data?.currentCards;
            if (cards == null || card == null) return 0;

            var count = 0;
            foreach (var owned in cards)
            {
                if (owned == null) continue;
                if (owned == card ||
                    string.Equals(owned.cardName, card.cardName, StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountOnBar(CardBar bar, CardInfo card)
        {
            if (bar == null || card == null) return 0;

            var count = 0;
            var field = AccessTools.Field(typeof(CardBarButton), "card");
            foreach (var button in bar.GetComponentsInChildren<CardBarButton>(true))
            {
                if (button == null) continue;
                var onBar = field?.GetValue(button) as CardInfo;
                if (onBar == null) continue;
                if (onBar == card ||
                    string.Equals(onBar.cardName, card.cardName, StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                }
            }

            return count;
        }
    }
}
