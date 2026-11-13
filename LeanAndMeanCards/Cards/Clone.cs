using LeanAndMeanCards.Utils;
using UnityEngine;

namespace LeanAndMeanCards.Cards
{
    public class Clone : MMCard
    {
        public const string Title = "Clone";
        internal static CardInfo Card;

        protected override string GetTitle() => Title;

        protected override string GetDescription() =>
            "Pick a card you already have. You get a copy of it now, and another copy at the end of every round for the rest of the game.";

        protected override CardInfo.Rarity GetRarity() => RarityHelper.Legendary;

        protected override GameObject GetCardArt() => CardArtFactory.Create("clone");

        // One cloning engine per player. A second Clone would need its own target and its
        // own round tick, and two of those end a game on their own.
        protected override bool AllowMultiple => false;

        protected override CardInfoStat[] GetStats() => new[]
        {
            CardStatApply.Stat(true, "a card you own", "Copy"),
            CardStatApply.Stat(true, "every round", "Again")
        };

        public override void OnAddCard(Player player, Gun gun, GunAmmo gunAmmo, CharacterData data, HealthHandler health,
            Gravity gravity, Block block, CharacterStatModifiers characterStats)
        {
            CardCopyManager.NotifyCopyCardGained(player, CardCopyKind.Clone);
        }
    }
}
