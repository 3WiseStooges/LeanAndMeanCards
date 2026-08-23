using LeanAndMeanCards.Utils;
using UnityEngine;

namespace LeanAndMeanCards.Cards
{
    public class JarOfDirt : MMCard
    {
        public const string Title = "Jar of Dirt";
        internal static CardInfo Card;

        protected override string GetTitle() => Title;

        protected override string GetDescription() =>
            "Replace every Null you currently own with a treasure. Only shows up if you have Nulls to convert. Disabled Nulls stay Nulls.";

        protected override CardInfo.Rarity GetRarity() => RarityHelper.Unique;

        protected override GameObject GetCardArt() => CardArtFactory.Create("jarofdirt");

        protected override CardInfoStat[] GetStats() => System.Array.Empty<CardInfoStat>();

        public override bool GetEnabled() => true;

        public override void OnAddCard(Player player, Gun gun, GunAmmo gunAmmo, CharacterData data, HealthHandler health,
            Gravity gravity, Block block, CharacterStatModifiers characterStats)
        {
            if (player?.data?.view == null || !player.data.view.IsMine) return;
            JarOfDirtManager.TryConvert(player);
        }
    }
}
