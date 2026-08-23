using UnityEngine;

namespace LeanAndMeanCards.Utils
{
    internal static class CardStatApply
    {
        internal static CardInfoStat Stat(bool positive, string stat, string amount) => new CardInfoStat
        {
            positive = positive,
            stat = stat,
            amount = amount,
            simepleAmount = CardInfoStat.SimpleAmount.notAssigned
        };

        internal static void AddAmmo(Gun gun, int amount)
        {
            if (gun == null) return;
            gun.ammo = amount;
            var ammo = gun.GetComponent<GunAmmo>() ?? gun.GetComponentInChildren<GunAmmo>(true);
            if (ammo != null) ammo.maxAmmo = amount;
        }
    }
}
