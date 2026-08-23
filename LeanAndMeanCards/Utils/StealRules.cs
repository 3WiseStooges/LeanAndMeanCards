using System;
using CardChoiceSpawnUniqueCardPatch.CustomCategories;
using LeanAndMeanCards.Cards;

namespace LeanAndMeanCards.Utils
{
    internal static class StealRules
    {
        internal static bool IsStealable(Player thief, Player victim, CardInfo card, out string reason)
        {
            reason = null;
            if (thief == null || victim == null || card == null)
            {
                reason = "Invalid";
                return false;
            }

            if (thief.playerID == victim.playerID)
            {
                reason = "Self";
                return false;
            }

            if (IsBlockedCard(card))
            {
                reason = "Blocked";
                return false;
            }

            if (RarityHelper.IsUniqueCard(card))
            {
                reason = "Unique";
                return false;
            }

            if (WwmCurses.IsCurse(card))
            {
                reason = "Curse";
                return false;
            }

            if (!card.allowMultiple && thief.data?.currentCards != null)
            {
                foreach (var owned in thief.data.currentCards)
                {
                    if (owned != null && string.Equals(owned.cardName, card.cardName, StringComparison.OrdinalIgnoreCase))
                    {
                        reason = "Already owned";
                        return false;
                    }
                }
            }

            if (IsNullPlaceholder(card))
            {
                reason = "Null";
                return false;
            }

            return true;
        }

        internal static bool IsBlockedCard(CardInfo card)
        {
            if (card == null) return true;

            var name = card.cardName ?? "";
            if (string.Equals(name, Thief.Title, StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(name, Takebacksies.Title, StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(name, SandbagSimulator.Title, StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(name, JarOfDirt.Title, StringComparison.OrdinalIgnoreCase)) return true;
            // Owned by MulliganMadness when installed; matched by name so this pack stays standalone.
            if (string.Equals(name, "Return to Sender", StringComparison.OrdinalIgnoreCase)) return true;

            if (card.categories != null)
            {
                foreach (var category in card.categories)
                {
                    if (category == null) continue;
                    var categoryName = category.name ?? "";
                    if (categoryName.IndexOf("Genie", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                }
            }

            return false;
        }

        private static bool IsNullPlaceholder(CardInfo card)
        {
            var name = (card.cardName ?? "").Trim();
            if (name.StartsWith("[]", StringComparison.Ordinal)) return true;
            if (name.Equals("null", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        internal static int CountStealableCards(Player thief, Player victim)
        {
            var cards = victim?.data?.currentCards;
            if (cards == null) return 0;

            var count = 0;
            foreach (var card in cards)
            {
                if (IsStealable(thief, victim, card, out _)) count++;
            }

            return count;
        }
    }
}
