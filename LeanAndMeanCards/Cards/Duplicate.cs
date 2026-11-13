using LeanAndMeanCards.Utils;
using UnityEngine;

namespace LeanAndMeanCards.Cards
{
    public class Duplicate : MMCard
    {
        public const string Title = "Duplicate";
        internal static CardInfo Card;

        protected override string GetTitle() => Title;

        protected override string GetDescription() =>
            "Pick a card you already have. A second copy of it goes straight into your hand. Extra copies each give another pick.";

        protected override CardInfo.Rarity GetRarity() => CardInfo.Rarity.Rare;

        protected override GameObject GetCardArt() => CardArtFactory.Create("duplicate");

        protected override bool AllowMultiple => true;

        protected override CardInfoStat[] GetStats() => new[]
        {
            CardStatApply.Stat(true, "a card you own", "Copy")
        };

        public override void OnAddCard(Player player, Gun gun, GunAmmo gunAmmo, CharacterData data, HealthHandler health,
            Gravity gravity, Block block, CharacterStatModifiers characterStats)
        {
            CardCopyManager.NotifyCopyCardGained(player, CardCopyKind.Duplicate);
        }
    }
}
