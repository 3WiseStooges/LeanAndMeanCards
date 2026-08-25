using LeanAndMeanCards.Utils;
using UnityEngine;

namespace LeanAndMeanCards.Cards
{
    public class SandbagSimulator : MMCard
    {
        public const string Title = "Sandbag Simulator";
        internal static CardInfo Card;

        protected override string GetTitle() => Title;

        protected override string GetDescription() =>
            "Reroll any player's cards - including your own. The pick waits until you choose. Host can limit uses per game.";

        protected override CardInfo.Rarity GetRarity() => RarityHelper.Legendary;

        protected override GameObject GetCardArt() => CardArtFactory.Create("sandbag");

        protected override CardInfoStat[] GetStats() => System.Array.Empty<CardInfoStat>();

        public override bool GetEnabled() => true;

        public override void OnAddCard(Player player, Gun gun, GunAmmo gunAmmo, CharacterData data, HealthHandler health,
            Gravity gravity, Block block, CharacterStatModifiers characterStats)
        {
            if (player?.data?.view == null || !player.data.view.IsMine) return;
            SandbagManager.TryPromptSandbag(player);
        }
    }
}
