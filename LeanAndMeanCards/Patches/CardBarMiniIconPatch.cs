using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using ModdingUtils.Utils;
using LeanAndMeanCards.Utils;
using UnboundLib;
using UnityEngine;
using CardsApi = ModdingUtils.Utils.Cards;

namespace LeanAndMeanCards.Patches
{
    [HarmonyPatch(typeof(CardBar), "AddCard")]
    [HarmonyPriority(Priority.Last)]
    internal static class CardBarMiniIconPatch
    {
        private static void Postfix(CardBar __instance)
        {
            StampMm(__instance);
        }

        // Five different hooks can report the same card addition (Cards.SilentAddToCardBar ->
        // CardBarUtils.SilentAddToCardBar -> CardBar.AddCard, plus FancyCardBar's own pass),
        // so the delayed passes are coalesced per bar.
        private static readonly HashSet<CardBar> PendingBars = new HashSet<CardBar>();
        private static readonly HashSet<CardBar> DirtyBars = new HashSet<CardBar>();

        internal static void StampMm(CardBar bar)
        {
            if (bar == null) return;
            CardBarMiniIcons.ApplyAllMmOnBar(bar);

            if (Unbound.Instance == null) return;

            // A pass is already in flight. Do NOT just drop this request: FancyCardBar and
            // the vanilla bar rebuild their buttons a few frames after a card is added, so a
            // card whose only stamp was the immediate one above gets its icon wiped and never
            // restored. That is what left mini icons missing when two cards landed close
            // together. Record it and run one more pass once the current one finishes.
            if (!PendingBars.Add(bar))
            {
                DirtyBars.Add(bar);
                return;
            }

            ScheduleRestamp(bar);
        }

        private static void ScheduleRestamp(CardBar bar)
        {
            Unbound.Instance.ExecuteAfterFrames(2, () =>
            {
                if (bar != null) CardBarMiniIcons.ApplyAllMmOnBar(bar);
            });

            Unbound.Instance.ExecuteAfterFrames(8, () =>
            {
                if (bar != null) CardBarMiniIcons.ApplyAllMmOnBar(bar);
                PendingBars.Remove(bar);

                if (!DirtyBars.Remove(bar) || bar == null || Unbound.Instance == null) return;
                PendingBars.Add(bar);
                ScheduleRestamp(bar);
            });
        }
    }

    [HarmonyPatch(typeof(CardInfo), "Awake")]
    internal static class CardInfoMiniSpritePatch
    {
        private static void Postfix(CardInfo __instance)
        {
            try
            {
                CardArtFactory.TryAssignSprite(__instance);
            }
            catch (Exception ex)
            {
                // Never throw out of CardInfo.Awake — aborts Photon card spawn online.
                Plugin.Instance?.LogWarn($"CardInfo mini sprite skipped: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(CardBarUtils))]
    internal static class CardBarUtilsMiniIconPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyPatch(nameof(CardBarUtils.SilentAddToCardBar), typeof(int), typeof(CardInfo), typeof(string))]
        private static void AfterSilentId(int playerID)
        {
            ApplyPlayer(playerID);
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyPatch(nameof(CardBarUtils.SilentAddToCardBar), typeof(Player), typeof(CardInfo), typeof(string))]
        private static void AfterSilentPlayer(Player player)
        {
            if (player != null) ApplyPlayer(player.playerID);
        }

        private static void ApplyPlayer(int playerID)
        {
            try
            {
                CardBarMiniIconPatch.StampMm(CardBarUtils.instance.PlayersCardBar(playerID));
            }
            catch
            {
            }
        }
    }

    [HarmonyPatch]
    internal static class CardsSilentAddMiniIconPatch
    {
        private static bool Prepare() => TargetMethod() != null;

        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(CardsApi),
                "SilentAddToCardBar",
                new[] { typeof(int), typeof(CardInfo), typeof(string), typeof(float), typeof(float) });
        }

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(int playerID)
        {
            try
            {
                CardBarMiniIconPatch.StampMm(CardBarUtils.instance.PlayersCardBar(playerID));
            }
            catch
            {
            }
        }
    }

    [HarmonyPatch]
    internal static class TabInfoMiniIconPatch
    {
        private static bool Prepare() => TargetMethod() != null;

        private static MethodBase TargetMethod()
        {
            var type = AccessTools.TypeByName("TabInfo.Utils.PlayerCardButton");
            return type == null ? null : AccessTools.Method(type, "Start");
        }

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(object __instance)
        {
            if (!(__instance is MonoBehaviour mb)) return;
            var field = AccessTools.Field(__instance.GetType(), "card");
            var card = field?.GetValue(__instance) as CardInfo;
            CardBarMiniIcons.Apply(mb.gameObject, card);
        }
    }

    [HarmonyPatch]
    internal static class FancyIconAdderMiniPatch
    {
        private static bool Prepare() => TargetMethod() != null;

        private static MethodBase TargetMethod()
        {
            var type = AccessTools.TypeByName("FancyCardBar.Patches.FancyIconAdder");
            return type == null ? null : AccessTools.Method(type, "addIcon", new[] { typeof(CardBar) });
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(CardBar __0)
        {
            CardBarMiniIconPatch.StampMm(__0);
        }
    }
}
