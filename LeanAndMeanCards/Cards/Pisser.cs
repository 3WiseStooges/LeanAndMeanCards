using LeanAndMeanCards.Utils;
using UnityEngine;

namespace LeanAndMeanCards.Cards
{
    public class Pisser : MMCard
    {
        public const string Title = "Pisser";
        internal static CardInfo Card;

        protected override string GetTitle() => Title;

        protected override string GetDescription() =>
            "+4 ammo, 40% faster fire, and no spread. 20% less damage.";

        protected override CardInfo.Rarity GetRarity() => CardInfo.Rarity.Uncommon;

        protected override GameObject GetCardArt() => CardArtFactory.Create("pisser");

        protected override CardInfoStat[] GetStats() => new[]
        {
            CardStatApply.Stat(true, "Ammo", "+4"),
            CardStatApply.Stat(true, "Attack speed", "+40%"),
            CardStatApply.Stat(false, "Damage", "-20%")
        };

        public override void SetupCard(CardInfo cardInfo, Gun gun, ApplyCardStats cardStats, CharacterStatModifiers statModifiers)
        {
            base.SetupCard(cardInfo, gun, cardStats, statModifiers);
            gun.damage = 0.8f;
            gun.attackSpeed = 0.714f;
            gun.spread = 0f;
            gun.evenSpread = 0f;
            gun.multiplySpread = 0f;
            CardStatApply.AddAmmo(gun, 4);
        }

        public override void OnAddCard(Player player, Gun gun, GunAmmo gunAmmo, CharacterData data, HealthHandler health,
            Gravity gravity, Block block, CharacterStatModifiers characterStats)
        {
            if (gun == null) return;
            gun.spread = 0f;
            gun.evenSpread = 0f;
            gun.multiplySpread = 0f;
        }
    }
}
