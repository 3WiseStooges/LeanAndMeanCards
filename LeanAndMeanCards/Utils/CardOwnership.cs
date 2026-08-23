using System;

namespace LeanAndMeanCards.Utils
{
    internal static class CardOwnership
    {
        internal const float KickbackForce = 1700f;

        /// <summary>
        /// Matches on the CardInfo instance first, then on name. The name fallback matters:
        /// a player's currentCards hold clones, and some mods (Genie, Null conversions)
        /// substitute the instance while keeping the name.
        /// </summary>
        internal static bool Has(Player player, CardInfo card)
        {
            if (player?.data?.currentCards == null || card == null) return false;
            foreach (var owned in player.data.currentCards)
            {
                if (owned == null) continue;
                if (owned == card) return true;
                if (!string.IsNullOrEmpty(card.cardName) &&
                    string.Equals(owned.cardName, card.cardName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        internal static int CountOf(Player player, CardInfo card)
        {
            if (player?.data?.currentCards == null || card == null) return 0;
            var count = 0;
            foreach (var owned in player.data.currentCards)
            {
                if (owned == null) continue;
                if (owned == card ||
                    (!string.IsNullOrEmpty(card.cardName) &&
                     string.Equals(owned.cardName, card.cardName, StringComparison.OrdinalIgnoreCase)))
                {
                    count++;
                }
            }

            return count;
        }
    }
}
