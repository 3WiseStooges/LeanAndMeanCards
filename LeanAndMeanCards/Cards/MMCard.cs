using CardChoiceSpawnUniqueCardPatch.CustomCategories;
using LeanAndMeanCards.Utils;
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
            // CopyGunStats multiplies gravity from the card template. Unbound Instantiates
            // templateCard, so a leftover value here would make every LAMC card feel like
            // Drop Grenade (bounces fall flat) even when this card does not touch gravity.
            if (gun != null) gun.gravity = 1f;
            // Local / Photon clones of CardInfo often drop cardArt tags. Register
            // the name here so the card bar can still find the mini PNG.
            CardArtFactory.TryAssignSprite(cardInfo);
        }

        public override void Callback()
        {
            CardArtFactory.TryAssignSprite(cardInfo);
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
