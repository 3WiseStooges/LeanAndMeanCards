using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using LeanAndMeanCards.Cards;
using UnityEngine;

namespace LeanAndMeanCards.Utils
{
    /// <summary>
    /// Pisser is a hold-to-spray gun, and it has to stay one whatever else is picked.
    ///
    /// Holding fire only repeats in WeaponHandler.Attack when
    ///     gun.attackSpeed / stats.attackSpeedMultiplier &lt; 0.3f
    /// and neither useCharge nor dontAllowAutoFire is set. Pisser's -28% cooldown drops a
    /// stock gun under that line, so it sprayed at first, but any later card that slows
    /// the gun (Big Bullet, Careful Planning, most sniper-style picks) pushed the cooldown
    /// back over 0.3 and the hold quietly turned into click-to-fire. The old fix only
    /// cleared the two flags, so it never covered that.
    ///
    /// Now the 0.3 cutoff itself is swapped for a per-player check: Pisser owners always
    /// pass it. Their gun still fires at its own cooldown (Gun.IsReady gates that); holding
    /// just keeps firing at whatever that rate is.
    ///
    /// Both checks run inside WeaponHandler.Attack every frame, so nothing has to be kept
    /// alive on the player. The old per-player ticker could be destroyed mid-reapply when
    /// a card removal briefly emptied currentCards, and never came back.
    /// </summary>
    internal static class PisserHoldFire
    {
        private const float VanillaAutoFireCutoff = 0.3f;

        private static readonly AccessTools.FieldRef<WeaponHandler, CharacterData> DataRef =
            AccessTools.FieldRefAccess<WeaponHandler, CharacterData>("data");

        /// <summary>Kept for Pisser.OnAddCard; clears the click-to-fire flags right away.</summary>
        internal static void Ensure(Player player)
        {
            var handler = player?.data?.weaponHandler;
            if (handler != null && Owns(handler)) ClearClickOnlyFlags(handler.gun);
        }

        internal static bool Owns(WeaponHandler handler)
        {
            if (handler == null) return false;
            CharacterData data = null;
            try
            {
                data = DataRef(handler);
            }
            catch
            {
            }

            var player = data != null ? data.player : handler.GetComponent<Player>();
            return player != null && CardOwnership.Has(player, Pisser.Card);
        }

        internal static void ClearClickOnlyFlags(Gun gun)
        {
            if (gun == null) return;
            gun.dontAllowAutoFire = false;
            gun.useCharge = false;
        }

        /// <summary>Stands in for the literal 0.3f in WeaponHandler.Attack.</summary>
        internal static float AutoFireCutoff(WeaponHandler handler) =>
            Owns(handler) ? float.MaxValue : VanillaAutoFireCutoff;
    }

    [HarmonyPatch(typeof(WeaponHandler), "Attack")]
    internal static class PisserAutoFirePatch
    {
        private static void Prefix(WeaponHandler __instance)
        {
            if (__instance != null && PisserHoldFire.Owns(__instance))
                PisserHoldFire.ClearClickOnlyFlags(__instance.gun);
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var cutoff = AccessTools.Method(typeof(PisserHoldFire), nameof(PisserHoldFire.AutoFireCutoff));
            var list = new List<CodeInstruction>(instructions);
            var replaced = 0;
            for (var i = 1; i < list.Count; i++)
            {
                var ins = list[i];
                // Only the constant compared against attackSpeed / attackSpeedMultiplier.
                if (ins.opcode != OpCodes.Ldc_R4 || !(ins.operand is float f) || Mathf.Abs(f - 0.3f) > 0.0001f)
                    continue;
                if (list[i - 1].opcode != OpCodes.Div) continue;

                // Rewritten in place so any labels / blocks on it stay put.
                ins.opcode = OpCodes.Ldarg_0;
                ins.operand = null;
                list.Insert(i + 1, new CodeInstruction(OpCodes.Call, cutoff));
                replaced++;
                i++;
            }

            if (replaced == 1)
                Plugin.Instance?.Log("Pisser: auto-fire cutoff patched");
            else
                Plugin.Instance?.LogWarn($"Pisser: expected 1 auto-fire cutoff in WeaponHandler.Attack, patched {replaced}");
            return list;
        }
    }
}
