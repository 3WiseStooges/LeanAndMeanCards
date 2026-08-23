using LeanAndMeanCards.Utils;
using UnityEngine;

namespace LeanAndMeanCards.Cards
{
    public class Takebacksies : MMCard
    {
        public const string Title = "Takebacksies";
        internal static CardInfo Card;

        protected override string GetTitle() => Title;

        protected override string GetDescription() =>
            "Someone stole from you? Yoink your card back from whoever is holding it now.";

        protected override CardInfo.Rarity GetRarity() => CardInfo.Rarity.Common;

        protected override bool AllowMultiple => true;

        protected override GameObject GetCardArt() => CardArtFactory.Create("takebacksies");

        protected override CardInfoStat[] GetStats() => System.Array.Empty<CardInfoStat>();

        public override bool GetEnabled() => true;

        public override void SetupCard(CardInfo cardInfo, Gun gun, ApplyCardStats cardStats, CharacterStatModifiers characterStats)
        {
            base.SetupCard(cardInfo, gun, cardStats, characterStats);
            cardInfo.categories = new[] { TakebacksiesOnlyCategory };
            cardInfo.blacklistedCategories = new CardCategory[0];
        }

        public override void OnAddCard(Player player, Gun gun, GunAmmo gunAmmo, CharacterData data, HealthHandler health,
            Gravity gravity, Block block, CharacterStatModifiers characterStats)
        {
            if (player?.data?.view == null || !player.data.view.IsMine) return;
            StealLedger.TryExecuteTakeback(player);
        }
    }
}
