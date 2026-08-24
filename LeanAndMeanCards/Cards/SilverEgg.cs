using LeanAndMeanCards.Utils;
using UnityEngine;

namespace LeanAndMeanCards.Cards
{
    public class SilverEgg : MMCard
    {
        public const string Title = "Silver Egg";
        internal static CardInfo Card;

        protected override string GetTitle() => Title;

        protected override string GetDescription() =>
            "Hatches after 2 rounds into a small random card haul (weaker and faster than The Golden Egg). Extra copies each hatch another.";

        protected override CardInfo.Rarity GetRarity() => CardInfo.Rarity.Rare;

        protected override GameObject GetCardArt() => CardArtFactory.Create("silveregg");

        protected override bool AllowMultiple => true;

        protected override CardThemeColor.CardThemeColorType GetTheme() =>
            CardThemeColor.CardThemeColorType.TechWhite;

        protected override CardInfoStat[] GetStats() => new[]
        {
            CardStatApply.Stat(true, "after 2 rounds", "Hatch"),
            CardStatApply.Stat(true, "1-2 cards", "Loot")
        };

        public override void OnAddCard(Player player, Gun gun, GunAmmo gunAmmo, CharacterData data, HealthHandler health,
            Gravity gravity, Block block, CharacterStatModifiers characterStats)
        {
            SilverEggManager.NotifyGained(player);
        }

        public override void OnRemoveCard(Player player, Gun gun, GunAmmo gunAmmo, CharacterData data, HealthHandler health,
            Gravity gravity, Block block, CharacterStatModifiers characterStats)
        {
            SilverEggManager.NotifyRemoved(player);
        }
    }
}
