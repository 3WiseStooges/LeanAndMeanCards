using LeanAndMeanCards.Utils;
using UnityEngine;

namespace LeanAndMeanCards.Cards
{
    public class BozoShoes : MMCard
    {
        public const string Title = "Bozo Shoes";
        internal const float KnockbackMultiplier = 1.5f;
        internal static CardInfo Card;

        protected override string GetTitle() => Title;

        protected override string GetDescription() =>
            "Players you hit wear clown shoes and take +50% knockback from everyone for the rest of the round.";

        protected override CardInfo.Rarity GetRarity() => CardInfo.Rarity.Uncommon;

        protected override GameObject GetCardArt() => CardArtFactory.Create("bozoshoes");

        protected override CardInfoStat[] GetStats() => new[]
        {
            CardStatApply.Stat(false, "Knockback", "+50%")
        };
    }
}
