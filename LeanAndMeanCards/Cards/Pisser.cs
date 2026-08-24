using LeanAndMeanCards.Utils;
using UnityEngine;

namespace LeanAndMeanCards.Cards
{
    public class Pisser : MMCard
    {
        public const string Title = "Pisser";
        internal static CardInfo Card;

        protected override string GetTitle() => Title;

        protected override string GetDescription() =>
            "+4 ammo, 40% faster fire, and tighter spread. 20% less damage.";

        protected override CardInfo.Rarity GetRarity() => CardInfo.Rarity.Uncommon;

        protected override GameObject GetCardArt() => CardArtFactory.Create("pisser");

        protected override CardInfoStat[] GetStats() => new[]
        {
            CardStatApply.Stat(true, "Ammo", "+4"),
            CardStatApply.Stat(true, "Attack speed", "+40%"),
            CardStatApply.Stat(true, "Spread", "-50%"),
            CardStatApply.Stat(false, "Damage", "-20%")
        };

        public override void SetupCard(CardInfo cardInfo, Gun gun, ApplyCardStats cardStats, CharacterStatModifiers statModifiers)
        {
            base.SetupCard(cardInfo, gun, cardStats, statModifiers);
            gun.damage = 0.8f;
            gun.attackSpeed = 0.714f;

            // Halve spread, never zero it.
            //
            // ApplyCardStats.CopyGunStats multiplies this field (copyToGun.multiplySpread *=
            // copyFromGun.multiplySpread), and Gun.GetShootDirection uses it as
            //     forward += cross * Random.Range(-spread, spread) * multiplySpread
            // so multiplySpread = 0 pins every projectile to the exact same vector. On any
            // multi-projectile build that stacks the whole volley on one point and it all
            // lands on the same frame — effectively damage x numberOfProjectiles. That read
            // as an "astronomical damage increase" on a card that is supposed to be a weak
            // fast spray, and being multiplicative it was permanent.
            //
            // gun.spread and gun.evenSpread are ADDITIVE here, so the old `= 0f` on those
            // did nothing at all.
            gun.multiplySpread = 0.5f;

            CardStatApply.AddAmmo(gun, 4);
        }

        public override void OnAddCard(Player player, Gun gun, GunAmmo gunAmmo, CharacterData data, HealthHandler health,
            Gravity gravity, Block block, CharacterStatModifiers characterStats)
        {
            PisserHoldFire.Ensure(player);
        }
    }
}
