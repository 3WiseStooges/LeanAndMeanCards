using HarmonyLib;
using LeanAndMeanCards.Utils;
using UnityEngine;

namespace LeanAndMeanCards.Patches
{
    // Pristine Perseverance uses StatsWhenFullHP: while above ~95% HP it multiplies max HP.
    // TemporaryStatsPatch replaces that with a stored additive delta. After an HP-reducing
    // card multiplies maxHealth, the delta is stale, shrink subtracts the old bonus, and
    // health collapses to a handful of HP. Restore vanilla multiply/divide, and peel the
    // bonus off before card stats so the new % applies to real max HP.
    internal static class PristineHealth
    {
        internal static bool Enabled => true;

        internal static void Strip(Player player)
        {
            if (!Enabled || player == null) return;

            var effects = player.GetComponentsInChildren<StatsWhenFullHP>(true);
            if (effects == null || effects.Length == 0) return;

            foreach (var effect in effects)
            {
                SetGrown(effect, false);
            }
        }

        internal static void SetGrown(StatsWhenFullHP effect, bool grown)
        {
            if (effect == null) return;

            var traverse = Traverse.Create(effect);
            var data = traverse.Field("data").GetValue<CharacterData>();
            if (data == null)
            {
                data = effect.GetComponentInParent<CharacterData>();
                if (data == null) return;
                traverse.Field("data").SetValue(data);
            }

            var isOn = traverse.Field("isOn").GetValue<bool>();
            if (isOn == grown) return;

            var healthMul = SafeMul(effect.healthMultiplier);
            var sizeMul = SafeMul(effect.sizeMultiplier);

            if (grown)
            {
                data.health *= healthMul;
                data.maxHealth *= healthMul;
                data.stats.sizeMultiplier *= sizeMul;
            }
            else
            {
                data.health /= healthMul;
                data.maxHealth /= healthMul;
                data.stats.sizeMultiplier /= sizeMul;
            }

            if (data.maxHealth < 1f) data.maxHealth = 1f;
            if (data.health < 1f) data.health = 1f;

            AccessTools.Method(typeof(CharacterStatModifiers), "ConfigureMassAndSize")
                ?.Invoke(data.stats, null);
            traverse.Field("isOn").SetValue(grown);
        }

        private static float SafeMul(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || Mathf.Abs(value) < 0.0001f)
            {
                return 1f;
            }

            return value;
        }
    }

    [HarmonyPatch(typeof(StatsWhenFullHP), "Update")]
    internal static class StatsWhenFullHPPatch
    {
        private struct Snapshot
        {
            public float Health;
            public float MaxHealth;
            public float Size;
            public bool IsOn;
            public bool Valid;
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(StatsWhenFullHP __instance, ref Snapshot __state)
        {
            __state = default;
            if (!PristineHealth.Enabled) return;

            var data = Traverse.Create(__instance).Field("data").GetValue<CharacterData>();
            if (data?.stats == null) return;

            __state = new Snapshot
            {
                Health = data.health,
                MaxHealth = data.maxHealth,
                Size = data.stats.sizeMultiplier,
                IsOn = Traverse.Create(__instance).Field("isOn").GetValue<bool>(),
                Valid = true
            };
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(StatsWhenFullHP __instance, Snapshot __state)
        {
            if (!PristineHealth.Enabled || !__state.Valid) return;

            var traverse = Traverse.Create(__instance);
            var data = traverse.Field("data").GetValue<CharacterData>();
            if (data?.stats == null) return;

            data.health = __state.Health;
            data.maxHealth = __state.MaxHealth;
            data.stats.sizeMultiplier = __state.Size;
            traverse.Field("isOn").SetValue(__state.IsOn);

            if (data.maxHealth <= 0f) return;
            var shouldGrow = data.health / data.maxHealth >= __instance.healthThreshold;
            PristineHealth.SetGrown(__instance, shouldGrow);
        }
    }

    [HarmonyPatch(typeof(ApplyCardStats), "ApplyStats")]
    internal static class ApplyCardStatsPristinePatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(ApplyCardStats __instance)
        {
            if (!PristineHealth.Enabled) return;
            var player = Traverse.Create(__instance).Field("playerToUpgrade").GetValue<Player>();
            PristineHealth.Strip(player);
        }
    }
}
