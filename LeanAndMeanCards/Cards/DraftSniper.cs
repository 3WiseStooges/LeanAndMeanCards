using LeanAndMeanCards.Utils;
using UnityEngine;

namespace LeanAndMeanCards.Cards
{
    public class DraftSniper : MMCard
    {
        public const string Title = "Draft Sniper";
        internal static CardInfo Card;

        protected override string GetTitle() => Title;

        protected override string GetDescription() =>
            "During someone else's pick, click a card in their offer to lock it. They can't pick that one. Extra copies give extra locks.";

        protected override CardInfo.Rarity GetRarity() => CardInfo.Rarity.Rare;

        protected override GameObject GetCardArt() => CardArtFactory.Create("draftsniper");

        protected override bool AllowMultiple => true;

        protected override CardInfoStat[] GetStats() => new[]
        {
            CardStatApply.Stat(true, "Lock", "+1")
        };

        public override void OnAddCard(Player player, Gun gun, GunAmmo gunAmmo, CharacterData data, HealthHandler health,
            Gravity gravity, Block block, CharacterStatModifiers characterStats)
        {
            DraftSniperManager.NotifyGained(player);
        }
    }
}
