using CardChoiceSpawnUniqueCardPatch.CustomCategories;
using UnboundLib.Cards;
using UnityEngine;

namespace LeanAndMeanCards.Cards
{
    public abstract class MMCard : CustomCard
    {
        public const string TakebacksiesOnlyCategoryName = "LeanAndMeanCards_TakebacksiesOnly";

        public static CardCategory TakebacksiesOnlyCategory =>
            CustomCardCategories.instance.CardCategory(TakebacksiesOnlyCategoryName);

        public override void SetupCard(CardInfo cardInfo, Gun gun, ApplyCardStats cardStats, CharacterStatModifiers statModifiers)
        {
            cardInfo.allowMultiple = AllowMultiple;
        }

        public override void OnAddCard(Player player, Gun gun, GunAmmo gunAmmo, CharacterData data, HealthHandler health,
            Gravity gravity, Block block, CharacterStatModifiers characterStats)
        {
        }

        protected virtual bool AllowMultiple => false;

        public override string GetModName() => Plugin.CardsMenuName;

        // Theme tracks rarity so Toggle Cards / pick borders read clearly.
        // Curses keep EvilPurple via AutoPickCurse.
        protected override CardThemeColor.CardThemeColorType GetTheme()
        {
            var rarity = GetRarity();
            if (rarity == CardInfo.Rarity.Common) return CardThemeColor.CardThemeColorType.TechWhite;
            if (rarity == CardInfo.Rarity.Uncommon) return CardThemeColor.CardThemeColorType.PoisonGreen;
            if (rarity == CardInfo.Rarity.Rare) return CardThemeColor.CardThemeColorType.DefensiveBlue;

            try
            {
                if (rarity == Utils.RarityHelper.Unique) return CardThemeColor.CardThemeColorType.MagicPink;
                if (rarity == Utils.RarityHelper.Legendary) return CardThemeColor.CardThemeColorType.FirepowerYellow;
            }
            catch
            {
                // RarityLib missing
            }

            return CardThemeColor.CardThemeColorType.FirepowerYellow;
        }
    }
}
