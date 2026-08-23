using LeanAndMeanCards.Utils;
using UnityEngine;

namespace LeanAndMeanCards.Cards
{
    public class Confetti : MMCard
    {
        public const string Title = "Confetti";
        internal static CardInfo Card;

        protected override string GetTitle() => Title;

        protected override string GetDescription() =>
            "+2 ammo and 25% faster fire. 10% less damage.";

        protected override CardInfo.Rarity GetRarity() => CardInfo.Rarity.Common;

        protected override GameObject GetCardArt() => CardArtFactory.Create("confetti");

        protected override CardInfoStat[] GetStats() => new[]
        {
            CardStatApply.Stat(true, "Ammo", "+2"),
            CardStatApply.Stat(true, "Attack speed", "+25%"),
            CardStatApply.Stat(false, "Damage", "-10%")
        };

        public override void SetupCard(CardInfo cardInfo, Gun gun, ApplyCardStats cardStats, CharacterStatModifiers statModifiers)
        {
            base.SetupCard(cardInfo, gun, cardStats, statModifiers);
            gun.damage = 0.9f;
            gun.attackSpeed = 0.8f;
            CardStatApply.AddAmmo(gun, 2);
        }
    }
}
