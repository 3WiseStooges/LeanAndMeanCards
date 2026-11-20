using LeanAndMeanCards.Utils;
using UnityEngine;

namespace LeanAndMeanCards.Cards
{
    public class Plummet : MMCard
    {
        public const string Title = "Plummet";
        internal static CardInfo Card;

        protected override string GetTitle() => Title;

        protected override string GetDescription() =>
            "Players you hit get spiked straight down into the floor, or into the bottom of the map. Extra copies double the speed.";

        protected override CardInfo.Rarity GetRarity() => CardInfo.Rarity.Rare;

        protected override GameObject GetCardArt() => CardArtFactory.Create("plummet");

        protected override bool AllowMultiple => true;

        protected override CardInfoStat[] GetStats() => new[]
        {
            CardStatApply.Stat(true, "Knockback", "Straight down"),
            CardStatApply.Stat(true, "Speed per extra copy", "x2")
        };
    }
}
