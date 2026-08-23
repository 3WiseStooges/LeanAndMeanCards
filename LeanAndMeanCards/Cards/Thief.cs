using LeanAndMeanCards.Utils;
using UnityEngine;

namespace LeanAndMeanCards.Cards
{
    public class Thief : MMCard
    {
        public const string Title = "Thief";
        internal static CardInfo Card;

        protected override string GetTitle() => Title;

        protected override string GetDescription() =>
            "Once per game, pick a player and steal one of their cards. Hood optional.";

        protected override CardInfo.Rarity GetRarity() => RarityHelper.Legendary;

        protected override GameObject GetCardArt() => CardArtFactory.Create("thief");

        protected override CardInfoStat[] GetStats() => System.Array.Empty<CardInfoStat>();

        public override bool GetEnabled() => true;

        public override void OnAddCard(Player player, Gun gun, GunAmmo gunAmmo, CharacterData data, HealthHandler health,
            Gravity gravity, Block block, CharacterStatModifiers characterStats)
        {
            if (player?.data?.view == null || !player.data.view.IsMine) return;
            StealLedger.TryPromptSteal(player);
        }
    }
}
