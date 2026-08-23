using LeanAndMeanCards.Utils;
using UnityEngine;

namespace LeanAndMeanCards.Cards
{
    public class Dynamite : MMCard
    {
        public const string Title = "Dynamite";
        internal const float DamageMult = 1.2f;
        internal const float BlastDelay = 0.9f;
        internal const float BlastRadius = 2.6f;
        internal const float BlastDamage = 12f;
        internal const float BlastForce = 48000f;
        internal const float BlastFlying = 2.8f;
        internal static CardInfo Card;

        protected override string GetTitle() => Title;

        protected override string GetDescription() =>
            "Bullets plant a small delayed blast on hit, including bounces. Weak boom, but players and loose objects in the radius go flying.";

        protected override CardInfo.Rarity GetRarity() => CardInfo.Rarity.Uncommon;

        protected override GameObject GetCardArt() => CardArtFactory.Create("dynamite");

        protected override CardThemeColor.CardThemeColorType GetTheme() =>
            CardThemeColor.CardThemeColorType.DestructiveRed;

        protected override CardInfoStat[] GetStats() => new[]
        {
            CardStatApply.Stat(true, "Damage", "+20%")
        };

        public override void SetupCard(CardInfo cardInfo, Gun gun, ApplyCardStats cardStats, CharacterStatModifiers statModifiers)
        {
            base.SetupCard(cardInfo, gun, cardStats, statModifiers);
            gun.damage = DamageMult;
            DynamiteBlast.ApplyToGun(gun);
        }

        public override void OnAddCard(Player player, Gun gun, GunAmmo gunAmmo, CharacterData data, HealthHandler health,
            Gravity gravity, Block block, CharacterStatModifiers characterStats)
        {
            DynamiteBlast.ApplyToGun(gun);
        }
    }
}
