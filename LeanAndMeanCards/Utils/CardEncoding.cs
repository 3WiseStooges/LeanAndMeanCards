using System;
using System.Linq;
using ModdingUtils.Utils;
using CardsApi = ModdingUtils.Utils.Cards;

namespace LeanAndMeanCards.Utils
{
    internal static class CardEncoding
    {
        internal static string Encode(CardInfo card)
        {
            if (card == null) return "";
            var objectName = card.gameObject != null ? StripClone(card.gameObject.name) : "";
            var cardName = card.cardName ?? "";
            return objectName + "\n" + cardName;
        }

        internal static void Decode(string payload, out string objectName, out string cardName)
        {
            objectName = payload ?? "";
            cardName = "";
            if (string.IsNullOrEmpty(payload)) return;
            var split = payload.IndexOf('\n');
            if (split < 0) return;
            objectName = payload.Substring(0, split);
            cardName = payload.Substring(split + 1);
        }

        internal static CardInfo Resolve(string payload)
        {
            Decode(payload, out var objectName, out var cardName);
            objectName = StripClone(objectName);

            CardInfo card = null;
            if (!string.IsNullOrEmpty(objectName))
            {
                try { card = CardsApi.instance.GetCardWithObjectName(objectName); }
                catch { /* ignore */ }
            }

            if (card == null && !string.IsNullOrEmpty(cardName))
            {
                try { card = CardsApi.instance.GetCardWithName(cardName); }
                catch { /* ignore */ }

                card ??= CardsApi.all?.FirstOrDefault(c =>
                    c != null && !string.IsNullOrEmpty(c.cardName)
                    && string.Equals(c.cardName, cardName, StringComparison.OrdinalIgnoreCase));
            }

            return card;
        }

        internal static string StripClone(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            const string clone = "(Clone)";
            while (name.EndsWith(clone, StringComparison.Ordinal))
            {
                name = name.Substring(0, name.Length - clone.Length).Trim();
            }

            return name.Trim();
        }

        internal static bool Matches(CardInfo card, string payload)
        {
            if (card == null || string.IsNullOrEmpty(payload)) return false;
            Decode(payload, out var objectName, out var cardName);
            objectName = StripClone(objectName);

            if (!string.IsNullOrEmpty(objectName) && card.gameObject != null)
            {
                if (string.Equals(StripClone(card.gameObject.name), objectName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            if (!string.IsNullOrEmpty(cardName))
            {
                return string.Equals(card.cardName, cardName, StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        internal static int FindCardIndex(Player player, string payload)
        {
            var cards = player?.data?.currentCards;
            if (cards == null) return -1;

            for (var i = 0; i < cards.Count; i++)
            {
                if (Matches(cards[i], payload)) return i;
            }

            return -1;
        }

        internal static Player FindHolder(string payload)
        {
            if (PlayerManager.instance?.players == null) return null;
            foreach (var player in PlayerManager.instance.players)
            {
                if (player == null) continue;
                if (FindCardIndex(player, payload) >= 0) return player;
            }

            return null;
        }
    }
}
