using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using LeanAndMeanCards.Cards;
using Photon.Pun;
using UnboundLib;
using UnboundLib.GameModes;
using UnboundLib.Networking;
using UnityEngine;
using CardsApi = ModdingUtils.Utils.Cards;

namespace LeanAndMeanCards.Utils
{
    internal static class SilverEggManager
    {
        // Faster than KeysCards' Golden Egg (3), but weaker loot.
        internal const int HatchRounds = 2;

        // Must NOT be a readonly struct: Unity Mono lacks IsReadOnlyAttribute and
        // Harmony PatchAll aborts the whole assembly if it sees one.
        private sealed class Hatch
        {
            public int PlayerId;
            public int RoundsLeft;
        }

        private static readonly List<Hatch> Pending = new List<Hatch>();
        private static bool _ignoringSilverCardRemoval;

        internal static void RegisterHooks()
        {
            GameModeManager.AddHook(GameModeHooks.HookGameStart, OnGameStart);
            GameModeManager.AddHook(GameModeHooks.HookRoundEnd, OnRoundEnd);
        }

        internal static void ResetForNewGame()
        {
            Pending.Clear();
            _ignoringSilverCardRemoval = false;
        }

        internal static int PendingCount(Player player)
        {
            if (player == null) return 0;
            var count = 0;
            foreach (var hatch in Pending)
            {
                if (hatch.PlayerId == player.playerID) count++;
            }

            return count;
        }

        internal static int NextHatchRounds(Player player)
        {
            if (player == null) return -1;
            var best = int.MaxValue;
            foreach (var hatch in Pending)
            {
                if (hatch.PlayerId != player.playerID) continue;
                if (hatch.RoundsLeft < best) best = hatch.RoundsLeft;
            }

            return best == int.MaxValue ? -1 : best;
        }

        internal static void NotifyGained(Player player)
        {
            if (player == null) return;
            // Simulacrum runs ApplyStats twice for one copy. Cap hatches to owned eggs.
            var owned = CountOwned(player);
            var tracked = PendingCount(player);
            if (tracked >= Math.Max(owned, 1)) return;
            Pending.Add(new Hatch { PlayerId = player.playerID, RoundsLeft = HatchRounds });
            if (player.data?.view == null || !player.data.view.IsMine) return;
            CardTargetUi.ShowToast($"Silver Egg: hatches into random cards in {HatchRounds} rounds.");
        }

        internal static void NotifyRemoved(Player player)
        {
            if (player == null) return;
            if (_ignoringSilverCardRemoval) return;

            for (var i = Pending.Count - 1; i >= 0; i--)
            {
                if (Pending[i].PlayerId != player.playerID) continue;
                Pending.RemoveAt(i);
                return;
            }
        }

        internal static string StatusText(Player player)
        {
            var pending = PendingCount(player);
            var next = NextHatchRounds(player);
            if (pending > 0 && next >= 0)
            {
                return pending == 1
                    ? "hatches in " + RoundWord(next)
                    : pending + " eggs, next in " + RoundWord(next);
            }

            return "";
        }

        internal static bool ShowStat(Player player) => PendingCount(player) > 0;

        private static string RoundWord(int n)
        {
            if (n <= 0) return "this pick";
            return n == 1 ? "1 round" : n + " rounds";
        }

        private static int CountOwned(Player player)
        {
            var cards = player?.data?.currentCards;
            if (cards == null) return 0;
            var count = 0;
            foreach (var card in cards)
            {
                if (card == null) continue;
                if (SilverEgg.Card != null && card == SilverEgg.Card) count++;
                else if (string.Equals(card.cardName, SilverEgg.Title, StringComparison.OrdinalIgnoreCase)) count++;
            }

            return count;
        }

        private static IEnumerator OnGameStart(IGameModeHandler gm)
        {
            ResetForNewGame();
            yield break;
        }

        private static IEnumerator OnRoundEnd(IGameModeHandler gm)
        {
            yield return null;
            TickHatches();
        }

        private static void TickHatches()
        {
            for (var i = Pending.Count - 1; i >= 0; i--)
            {
                var hatch = Pending[i];
                hatch.RoundsLeft--;
                if (hatch.RoundsLeft > 0) continue;

                Pending.RemoveAt(i);
                HatchNow(hatch.PlayerId);
            }
        }

        private static void HatchNow(int playerId)
        {
            // Roll once on the host, sync the same loot. Remotes used to each roll a
            // different hand.
            if (!(PhotonNetwork.OfflineMode || PhotonNetwork.IsMasterClient)) return;
            var hatchPlayer = PickPhase.FindPlayer(playerId);
            if (hatchPlayer == null) return;
            var grants = BuildSilverLoot(hatchPlayer);
            var payloads = new List<string>();
            foreach (var card in grants)
            {
                if (card == null) continue;
                payloads.Add(CardEncoding.Encode(card));
            }

            NetworkingManager.RPC(typeof(SilverEggManager), nameof(RPCA_HatchSilver), playerId, payloads.ToArray());
        }

        [UnboundRPC]
        public static void RPCA_HatchSilver(int playerId, string[] payloads)
        {
            var player = PickPhase.FindPlayer(playerId);
            if (player == null)
            {
                Plugin.Instance?.LogWarn("Silver Egg hatch failed - player missing.");
                return;
            }

            var grants = new List<CardInfo>();
            if (payloads != null)
            {
                foreach (var payload in payloads)
                {
                    var card = CardEncoding.Resolve(payload);
                    if (card != null) grants.Add(card);
                }
            }

            if (PhotonNetwork.OfflineMode || PhotonNetwork.IsMasterClient)
            {
                RemoveOneSilverEgg(player);
                foreach (var card in grants)
                {
                    if (card == null) continue;
                    CardsApi.instance.AddCardToPlayer(player, card, false, "", 2f, 2f, true);
                }
            }

            if (player.data?.view != null && player.data.view.IsMine)
            {
                var names = string.Join(", ", grants.Where(c => c != null).Select(c => c.cardName));
                CardTargetUi.ShowToast(
                    grants.Count == 0
                        ? "Silver Egg hatched, but no cards were available."
                        : $"Silver Egg hatched: {names}");
            }

            Plugin.Instance?.Log($"Silver Egg hatched for player {playerId} -> {grants.Count} card(s).");
        }

        // Weaker than Keys Golden Egg (3-4 cards / rare / treasure / blessing rolls).
        // Roll 0-99: 55% one common, 30% two commons, 15% one uncommon.
        private static List<CardInfo> BuildSilverLoot(Player player)
        {
            var roll = UnityEngine.Random.Range(0, 100);
            var grants = new List<CardInfo>();
            if (roll < 55)
            {
                AddRandomOfRarity(player, grants, CardInfo.Rarity.Common, 1);
            }
            else if (roll < 85)
            {
                AddRandomOfRarity(player, grants, CardInfo.Rarity.Common, 2);
            }
            else
            {
                AddRandomOfRarity(player, grants, CardInfo.Rarity.Uncommon, 1);
            }

            return grants;
        }

        private static void AddRandomOfRarity(Player player, List<CardInfo> into, CardInfo.Rarity rarity, int count)
        {
            for (var n = 0; n < count; n++)
            {
                var card = PickRandom(player, rarity, into);
                if (card != null) into.Add(card);
            }
        }

        private static CardInfo PickRandom(Player player, CardInfo.Rarity rarity, List<CardInfo> already)
        {
            var all = CardsApi.all;
            if (all == null || all.Count == 0) return null;

            var options = new List<CardInfo>();
            foreach (var card in all)
            {
                if (card == null) continue;
                if (card.rarity != rarity) continue;
                if (IsBlockedHatchCard(card)) continue;
                if (already.Contains(card)) continue;
                if (!CardPool.IsActive(card)) continue;

                try
                {
                    if (!CardsApi.instance.PlayerIsAllowedCard(player, card)) continue;
                }
                catch
                {
                    continue;
                }

                options.Add(card);
            }

            if (options.Count == 0) return null;
            return options[UnityEngine.Random.Range(0, options.Count)];
        }

        private static bool IsBlockedHatchCard(CardInfo card)
        {
            if (SilverEgg.Card != null && card == SilverEgg.Card) return true;
            var name = card.cardName ?? "";
            if (name.Equals(SilverEgg.Title, StringComparison.OrdinalIgnoreCase)) return true;
            if (name.Equals("Nest Egg", StringComparison.OrdinalIgnoreCase)) return true;
            if (name.Equals("The Golden Egg", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static void RemoveOneSilverEgg(Player player)
        {
            var cards = player.data?.currentCards;
            if (cards == null) return;

            for (var i = cards.Count - 1; i >= 0; i--)
            {
                var card = cards[i];
                if (card == null) continue;
                var match = (SilverEgg.Card != null && card == SilverEgg.Card)
                            || string.Equals(card.cardName, SilverEgg.Title, StringComparison.OrdinalIgnoreCase);
                if (!match) continue;

                _ignoringSilverCardRemoval = true;
                try
                {
                    CardsApi.instance.RemoveCardFromPlayer(player, i);
                }
                finally
                {
                    _ignoringSilverCardRemoval = false;
                }

                return;
            }
        }
    }
}
