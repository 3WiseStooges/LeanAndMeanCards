using LeanAndMeanCards.Utils;
using UnityEngine;

namespace LeanAndMeanCards.Cards
{
    public class TaserTaserTaser : MMCard
    {
        public const string Title = "TASER TASER TASER";
        internal const float ExtraStunSeconds = 0.5f;
        internal static CardInfo Card;

        protected override string GetTitle() => Title;

        protected override string GetDescription() =>
            "Hits stun the target for 0.5 seconds, including catching your own shot. 15% faster fire. -1 ammo.";

        protected override CardInfo.Rarity GetRarity() => CardInfo.Rarity.Rare;

        protected override GameObject GetCardArt() => CardArtFactory.Create("tasertasertaser");

        protected override CardInfoStat[] GetStats() => new[]
        {
            CardStatApply.Stat(true, "Stun", "+0.5s"),
            CardStatApply.Stat(true, "Attack speed", "+15%"),
            CardStatApply.Stat(false, "Ammo", "-1")
        };

        public override void SetupCard(CardInfo cardInfo, Gun gun, ApplyCardStats cardStats, CharacterStatModifiers statModifiers)
        {
            base.SetupCard(cardInfo, gun, cardStats, statModifiers);
            gun.attackSpeed = 0.87f;
            CardStatApply.AddAmmo(gun, -1);
        }
    }
}
