using System;
using System.Collections.Generic;
using System.Linq;
using CardChoiceSpawnUniqueCardPatch.CustomCategories;
using ModdingUtils.Extensions;
using ModdingUtils.Utils;
using CardsApi = ModdingUtils.Utils.Cards;
using LeanAndMeanCards.Cards;
using Photon.Pun;
using UnboundLib;
using UnboundLib.Networking;
using UnityEngine;

namespace LeanAndMeanCards.Utils
{
    public static class StealLedger
    {
        private static readonly HashSet<int> ThiefUsedThisGame = new HashSet<int>();
        private static readonly HashSet<int> PendingThiefPrompt = new HashSet<int>();
        private static readonly Dictionary<int, string> PendingTakebackByVictim = new Dictionary<int, string>();

        internal static void ResetForNewGame()
        {
            ThiefUsedThisGame.Clear();
            PendingThiefPrompt.Clear();
            PendingTakebackByVictim.Clear();
            TakebacksiesBlacklist.EnsureGlobalBlacklist();
        }

        internal static bool HasUsedThief(Player player) =>
            player != null && ThiefUsedThisGame.Contains(player.playerID);

        internal static bool HasAnyStealableTarget(Player thief)
        {
            if (thief == null || PlayerManager.instance?.players == null) return false;

            foreach (var other in PlayerManager.instance.players)
            {
                if (other == null || other.playerID == thief.playerID) continue;
                if (StealRules.CountStealableCards(thief, other) > 0) return true;
            }

            return false;
        }

        internal static void TryPromptSteal(Player thief)
        {
            if (thief == null) return;
            if (HasUsedThief(thief))
            {
                Plugin.Instance.Log($"Player {thief.playerID} already used Thief this game.");
                return;
            }

            if (!HasAnyStealableTarget(thief))
            {
                PendingThiefPrompt.Add(thief.playerID);
                PlayerNotice.Show(thief, "Nobody has cards to steal yet.");
                Plugin.Instance.Log($"Thief deferred for player {thief.playerID} - no stealable targets.");
                return;
            }

            PendingThiefPrompt.Remove(thief.playerID);
            StealUi.TryOpen(thief);
        }

        internal static void TryOpenDeferredThiefPrompt()
        {
            var picker = PickPhase.GetCurrentPicker();
            if (picker?.data?.view == null || !picker.data.view.IsMine) return;
            if (!PendingThiefPrompt.Contains(picker.playerID)) return;
            if (HasUsedThief(picker)) return;
            if (!HasAnyStealableTarget(picker)) return;

            PendingThiefPrompt.Remove(picker.playerID);
            StealUi.TryOpen(picker);
        }

        internal static void OnStealUiOpened(Player thief)
        {
            PendingThiefPrompt.Remove(thief.playerID);
        }

        internal static void OnStealUiClosedWithoutSteal(Player thief)
        {
            if (HasUsedThief(thief)) return;
            if (!HasAnyStealableTarget(thief)) PendingThiefPrompt.Add(thief.playerID);
        }

        internal static void RequestSteal(Player thief, Player victim, CardInfo card)
        {
            if (thief == null || victim == null || card == null) return;
            if (!StealRules.IsStealable(thief, victim, card, out var reason))
            {
                PlayerNotice.Show(thief, reason ?? "Can't steal that card.");
                return;
            }

            var index = CardEncoding.FindCardIndex(victim, CardEncoding.Encode(card));
            if (index < 0)
            {
                PlayerNotice.Show(thief, "That card is gone.");
                return;
            }

            NetworkingManager.RPC(
                typeof(StealLedger),
                nameof(RPCA_ExecuteSteal),
                thief.playerID,
                victim.playerID,
                CardEncoding.Encode(card),
                index);
        }

        [UnboundRPC]
        public static void RPCA_ExecuteSteal(int thiefId, int victimId, string payload, int victimCardIndex)
        {
            if (!(PhotonNetwork.OfflineMode || PhotonNetwork.IsMasterClient)) return;

            var thief = PlayerManager.instance.players.FirstOrDefault(p => p.playerID == thiefId);
            var victim = PlayerManager.instance.players.FirstOrDefault(p => p.playerID == victimId);
            if (thief == null || victim == null)
            {
                Plugin.Instance.LogWarn("Steal RPC failed - player missing.");
                NotifyStealResult(thiefId, false, "Steal failed.");
                return;
            }

            if (ThiefUsedThisGame.Contains(thiefId))
            {
                NotifyStealResult(thiefId, false, "Thief already used this game.");
                return;
            }

            var card = CardEncoding.Resolve(payload);
            if (card == null)
            {
                Plugin.Instance.LogWarn("Steal RPC failed - card missing.");
                NotifyStealResult(thiefId, false, "That card is gone.");
                return;
            }

            if (!StealRules.IsStealable(thief, victim, card, out _))
            {
                Plugin.Instance.LogWarn("Steal RPC rejected - card not stealable.");
                NotifyStealResult(thiefId, false, "Can't steal that card.");
                return;
            }

            var liveIndex = CardEncoding.FindCardIndex(victim, payload);
            if (liveIndex < 0) liveIndex = victimCardIndex;
            if (liveIndex < 0 || victim.data?.currentCards == null || liveIndex >= victim.data.currentCards.Count)
            {
                Plugin.Instance.LogWarn("Steal RPC failed - card index invalid.");
                NotifyStealResult(thiefId, false, "That card is gone.");
                return;
            }

            CardsApi.instance.RemoveCardFromPlayer(victim, liveIndex);
            CardsApi.instance.AddCardToPlayer(thief, card, false, "", 2f, 2f, true);

            NetworkingManager.RPC(typeof(StealLedger), nameof(RPCA_SyncStealState), thiefId, victimId, payload);
            NotifyStealResult(thiefId, true, $"Stole {card.cardName}.");

            Plugin.Instance.Log($"Player {thiefId} stole '{card.cardName}' from player {victimId}.");
        }

        [UnboundRPC]
        public static void RPCA_SyncStealState(int thiefId, int victimId, string payload)
        {
            ThiefUsedThisGame.Add(thiefId);
            PendingThiefPrompt.Remove(thiefId);
            if (!string.IsNullOrEmpty(payload))
            {
                PendingTakebackByVictim[victimId] = payload;
            }
        }

        [UnboundRPC]
        public static void RPCA_StealResult(int thiefId, bool ok, string message)
        {
            var thief = PlayerManager.instance.players.FirstOrDefault(p => p.playerID == thiefId);
            if (thief?.data?.view == null || !thief.data.view.IsMine) return;

            StealUi.OnStealResult(ok, message);
        }

        private static void NotifyStealResult(int thiefId, bool ok, string message)
        {
            NetworkingManager.RPC(typeof(StealLedger), nameof(RPCA_StealResult), thiefId, ok, message ?? "");
        }

        internal static bool HasPendingTakeback(int victimId) =>
            PendingTakebackByVictim.ContainsKey(victimId);

        internal static string GetPendingTakebackPayload(int victimId)
        {
            PendingTakebackByVictim.TryGetValue(victimId, out var payload);
            return payload;
        }

        internal static void TryExecuteTakeback(Player victim)
        {
            if (victim == null) return;
            if (!PendingTakebackByVictim.ContainsKey(victim.playerID))
            {
                PlayerNotice.Show(victim, "Nothing to take back.");
                return;
            }

            NetworkingManager.RPC(typeof(StealLedger), nameof(RPCA_ExecuteTakeback), victim.playerID);
        }

        [UnboundRPC]
        public static void RPCA_ExecuteTakeback(int victimId)
        {
            var victim = PlayerManager.instance.players.FirstOrDefault(p => p.playerID == victimId);
            if (victim == null) return;
            if (!(PhotonNetwork.OfflineMode || PhotonNetwork.IsMasterClient)) return;

            if (!PendingTakebackByVictim.TryGetValue(victimId, out var payload) || string.IsNullOrEmpty(payload))
            {
                NetworkingManager.RPC(typeof(StealLedger), nameof(RPCA_SyncTakebackCleared), victimId, "Nothing to take back.");
                return;
            }

            var holder = CardEncoding.FindHolder(payload);
            if (holder == null)
            {
                Plugin.Instance.LogWarn($"Takeback cleared - stolen card no longer exists for victim {victimId}.");
                NetworkingManager.RPC(typeof(StealLedger), nameof(RPCA_SyncTakebackCleared), victimId, "Stolen card no longer exists.");
                return;
            }

            var index = CardEncoding.FindCardIndex(holder, payload);
            if (index < 0)
            {
                NetworkingManager.RPC(typeof(StealLedger), nameof(RPCA_SyncTakebackCleared), victimId, "Stolen card no longer exists.");
                return;
            }

            var card = CardEncoding.Resolve(payload);
            if (card == null)
            {
                NetworkingManager.RPC(typeof(StealLedger), nameof(RPCA_SyncTakebackCleared), victimId, "Stolen card no longer exists.");
                return;
            }

            CardsApi.instance.RemoveCardFromPlayer(holder, index);
            CardsApi.instance.AddCardToPlayer(victim, card, false, "", 2f, 2f, true);
            NetworkingManager.RPC(typeof(StealLedger), nameof(RPCA_SyncTakebackCleared), victimId, $"Took back {card.cardName}.");

            Plugin.Instance.Log($"Player {victimId} took back '{card.cardName}' from player {holder.playerID}.");
        }

        [UnboundRPC]
        public static void RPCA_SyncTakebackCleared(int victimId, string message)
        {
            PendingTakebackByVictim.Remove(victimId);
            if (string.IsNullOrEmpty(message)) return;

            var victim = PlayerManager.instance.players.FirstOrDefault(p => p.playerID == victimId);
            if (victim?.data?.view != null && victim.data.view.IsMine)
            {
                PlayerNotice.Show(victim, message);
            }
        }
    }

    internal static class TakebacksiesBlacklist
    {
        internal static void EnsureGlobalBlacklist()
        {
            if (Takebacksies.Card == null) return;
            if (PlayerManager.instance?.players == null) return;
            if (CustomCardCategories.instance == null) return;

            var category = MMCard.TakebacksiesOnlyCategory;
            if (category == null) return;

            foreach (var player in PlayerManager.instance.players)
            {
                if (player?.data?.stats == null) continue;
                try
                {
                    var list = player.data.stats.GetAdditionalData().blacklistedCategories;
                    if (list != null && !list.Contains(category)) list.Add(category);
                }
                catch
                {
                    // ignore
                }
            }
        }
    }

    internal static class PlayerNotice
    {
        internal static void Show(Player player, string message)
        {
            if (player?.data?.view == null || !player.data.view.IsMine) return;
            Plugin.Instance.Log(message);
            CardTargetUi.ShowToast(message);
        }
    }
}
