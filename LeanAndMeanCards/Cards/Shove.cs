using LeanAndMeanCards.Utils;
using UnityEngine;

namespace LeanAndMeanCards.Cards
{
    public class Shove : MMCard
    {
        public const string Title = "Shove";
        internal static CardInfo Card;

        protected override string GetTitle() => Title;

        protected override string GetDescription() =>
            "+40% bullet knockback and +25% damage.";

        protected override CardInfo.Rarity GetRarity() => CardInfo.Rarity.Common;

        protected override GameObject GetCardArt() => CardArtFactory.Create("shove");

        protected override CardInfoStat[] GetStats() => new[]
        {
            CardStatApply.Stat(true, "Knockback", "+40%"),
            CardStatApply.Stat(true, "Damage", "+25%")
        };

        public override void SetupCard(CardInfo cardInfo, Gun gun, ApplyCardStats cardStats, CharacterStatModifiers statModifiers)
        {
            base.SetupCard(cardInfo, gun, cardStats, statModifiers);
            gun.damage = 1.25f;
            gun.knockback = 1.4f;
        }
    }
}
