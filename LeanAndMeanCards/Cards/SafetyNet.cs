using LeanAndMeanCards.Utils;
using UnityEngine;

namespace LeanAndMeanCards.Cards
{
    public class SafetyNet : MMCard
    {
        public const string Title = "Safety Net";
        internal static CardInfo Card;

        protected override string GetTitle() => Title;

        protected override string GetDescription() =>
            "Map edges no longer deal damage.";

        protected override CardInfo.Rarity GetRarity() => CardInfo.Rarity.Rare;

        protected override GameObject GetCardArt() => CardArtFactory.Create("safetynet");

        protected override CardInfoStat[] GetStats() => System.Array.Empty<CardInfoStat>();
    }
}
