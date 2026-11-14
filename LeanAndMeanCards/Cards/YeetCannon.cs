using LeanAndMeanCards.Utils;
using UnityEngine;

namespace LeanAndMeanCards.Cards
{
    public class YeetCannon : MMCard
    {
        public const string Title = "Yeet Cannon";
        internal static CardInfo Card;

        protected override string GetTitle() => Title;

        protected override string GetDescription() =>
            "+100% bullet knockback and +15% damage. Everything you hit goes flying.";

        protected override CardInfo.Rarity GetRarity() => CardInfo.Rarity.Rare;

        protected override GameObject GetCardArt() => CardArtFactory.Create("yeetcannon");

        protected override CardInfoStat[] GetStats() => new[]
        {
            CardStatApply.Stat(true, "Knockback", "+100%"),
            CardStatApply.Stat(true, "Damage", "+15%")
        };

        public override void SetupCard(CardInfo cardInfo, Gun gun, ApplyCardStats cardStats, CharacterStatModifiers statModifiers)
        {
            base.SetupCard(cardInfo, gun, cardStats, statModifiers);
            gun.damage = 1.15f;
            gun.knockback = 2f;
        }
    }
}
