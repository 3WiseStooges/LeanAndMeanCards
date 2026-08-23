using LeanAndMeanCards.Utils;
using UnityEngine;

namespace LeanAndMeanCards.Cards
{
    public class Doorstop : MMCard
    {
        public const string Title = "Doorstop";
        internal static CardInfo Card;

        protected override string GetTitle() => Title;

        protected override string GetDescription() =>
            "+1 block. Block cooldown is 20% longer.";

        protected override CardInfo.Rarity GetRarity() => CardInfo.Rarity.Uncommon;

        protected override GameObject GetCardArt() => CardArtFactory.Create("doorstop");

        protected override CardInfoStat[] GetStats() => new[]
        {
            CardStatApply.Stat(true, "Blocks", "+1"),
            CardStatApply.Stat(false, "Block cooldown", "+20%")
        };

        public override void SetupCard(CardInfo cardInfo, Gun gun, ApplyCardStats cardStats, CharacterStatModifiers statModifiers, Block block)
        {
            base.SetupCard(cardInfo, gun, cardStats, statModifiers, block);
            if (block == null)
            {
                block = cardStats != null
                    ? cardStats.GetComponent<Block>() ?? cardStats.GetComponentInChildren<Block>(true)
                    : null;
                block ??= cardInfo.GetComponent<Block>() ?? cardInfo.GetComponentInChildren<Block>(true);
            }

            if (block == null) return;
            block.additionalBlocks = 1;
            block.cdMultiplier = 1.2f;
        }
    }
}
