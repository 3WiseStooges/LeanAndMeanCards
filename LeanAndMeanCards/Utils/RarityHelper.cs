using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace LeanAndMeanCards.Utils
{
    internal static class RarityHelper
    {
        internal static CardInfo.Rarity Legendary
        {
            get
            {
                var rarity = TryGetNamedRarity("Legendary");
                return rarity ?? CardInfo.Rarity.Rare;
            }
        }

        internal static CardInfo.Rarity Unique =>
            TryGetNamedRarity("Unique") ?? Legendary;

        internal static Color GetRarityColor(CardInfo card, bool selected = true)
        {
            if (card == null) return Color.white;

            try
            {
                var type = AccessTools.TypeByName("RarityLib.Utils.RarityUtils");
                var getData = type == null ? null : AccessTools.Method(type, "GetRarityData", new[] { typeof(CardInfo.Rarity) });
                if (getData == null) return DefaultRarityColor(card.rarity);

                var data = getData.Invoke(null, new object[] { card.rarity });
                if (data == null) return DefaultRarityColor(card.rarity);

                var field = selected ? AccessTools.Field(data.GetType(), "color") : AccessTools.Field(data.GetType(), "colorOff");
                if (field?.GetValue(data) is Color c) return c;
            }
            catch
            {
                // RarityLib not present
            }

            return DefaultRarityColor(card.rarity);
        }

        internal static bool IsUniqueCard(CardInfo card)
        {
            if (card == null) return false;

            try
            {
                var type = AccessTools.TypeByName("RarityLib.Utils.RarityUtils");
                var getData = type == null ? null : AccessTools.Method(type, "GetRarityData", new[] { typeof(CardInfo.Rarity) });
                if (getData == null) return false;

                var data = getData.Invoke(null, new object[] { card.rarity });
                var nameField = data?.GetType().GetField("name") ?? AccessTools.Field(data?.GetType(), "name");
                if (nameField?.GetValue(data) is string name)
                {
                    return string.Equals(name, "Unique", System.StringComparison.OrdinalIgnoreCase);
                }
            }
            catch
            {
                // ignore
            }

            return false;
        }

        private static CardInfo.Rarity? TryGetNamedRarity(string name)
        {
            try
            {
                var type = AccessTools.TypeByName("RarityLib.Utils.RarityUtils");
                var method = type == null ? null : AccessTools.Method(type, "GetRarity", new[] { typeof(string) });
                if (method == null) return null;
                return (CardInfo.Rarity)method.Invoke(null, new object[] { name });
            }
            catch
            {
                return null;
            }
        }

        private static Color DefaultRarityColor(CardInfo.Rarity rarity)
        {
            switch (rarity)
            {
                case CardInfo.Rarity.Common: return new Color(0.75f, 0.75f, 0.75f);
                case CardInfo.Rarity.Uncommon: return new Color(0.35f, 0.85f, 0.45f);
                case CardInfo.Rarity.Rare: return new Color(0.45f, 0.55f, 1f);
                default: return new Color(0.95f, 0.82f, 0.35f);
            }
        }
    }
}
