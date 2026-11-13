using System;
using LeanAndMeanCards.Cards;

namespace LeanAndMeanCards.Utils
{
    /// <summary>
    /// What Duplicate and Clone are allowed to copy out of a player's own hand.
    ///
    /// Deliberately narrower than "anything you hold". The two rules that matter:
    /// a copy must be worth having (a card whose whole effect is a one-shot prompt
    /// tracked per player gives nothing the second time), and a copy must be safe to
    /// hand out at round end, when no pick is running and no overlay may open.
    /// </summary>
    internal static class CardCopyRules
    {
        internal static bool IsCopyable(Player owner, CardInfo card, out string reason)
        {
            reason = null;

            if (owner == null || card == null)
            {
                reason = "Invalid";
                return false;
            }

            if (IsNullPlaceholder(card))
            {
                reason = "Null";
                return false;
            }

            // No copies of copies. Clone granting another Duplicate would pop a card
            // chooser at round end, and a cloned Clone compounds every round.
            if (IsCopyCard(card))
            {
                reason = "No copying this";
                return false;
            }

            if (WwmCurses.IsCurse(card))
            {
                reason = "Curse";
                return false;
            }

            // Unique means one of a kind. Thief already refuses to move these.
            if (RarityHelper.IsUniqueCard(card))
            {
                reason = "Unique";
                return false;
            }

            // Thief's blocklist is the same set for the same reason: cards whose effect
            // is a per-player one-shot (Thief, Sandbag, Jar of Dirt, Return to Sender)
            // or a Genie wish, none of which survive being handed out twice.
            if (StealRules.IsBlockedCard(card))
            {
                reason = "One use only";
                return false;
            }

            return true;
        }

        internal static bool IsCopyCard(CardInfo card)
        {
            if (card == null) return false;
            if (Duplicate.Card != null && card == Duplicate.Card) return true;
            if (Clone.Card != null && card == Clone.Card) return true;

            var name = card.cardName ?? "";
            if (string.Equals(name, Duplicate.Title, StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(name, Clone.Title, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        internal static int CountCopyable(Player owner)
        {
            var cards = owner?.data?.currentCards;
            if (cards == null) return 0;

            var count = 0;
            foreach (var card in cards)
            {
                if (IsCopyable(owner, card, out _)) count++;
            }

            return count;
        }

        private static bool IsNullPlaceholder(CardInfo card)
        {
            var name = (card.cardName ?? "").Trim();
            if (name.StartsWith("[]", StringComparison.Ordinal)) return true;
            if (name.Equals("null", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
