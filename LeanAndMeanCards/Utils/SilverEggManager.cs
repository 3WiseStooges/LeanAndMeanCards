using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LeanAndMeanCards.Cards;
using ModdingUtils.Utils;
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
        private static readonly List<BarDebt> PendingBar = new List<BarDebt>();
        private static bool _ignoringSilverCardRemoval;

        private sealed class BarDebt
        {
            public int PlayerId;
            public CardInfo Card;
        }

        internal static void RegisterHooks()
        {
            GameModeManager.AddHook(GameModeHooks.HookGameStart, OnGameStart);
            GameModeManager.AddHook(GameModeHooks.HookRoundEnd, OnRoundEnd);
            GameModeManager.AddHook(GameModeHooks.HookPickStart, OnPickStart);
            GameModeManager.AddHook(GameModeHooks.HookPlayerPickStart, OnPlayerPickStart);
            GameModeManager.AddHook(GameModeHooks.HookPointStart, OnPointStart);
        }

        internal static void ResetForNewGame()
        {
            Pending.Clear();
            PendingBar.Clear();
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
            Plugin.Instance?.Log($"Silver Egg gained by player {player.playerID} ({PlayerLabels.For(player)}).");
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

        private static IEnumerator OnPickStart(IGameModeHandler gm)
        {
            FlushBarDebt();
            yield break;
        }

        private static IEnumerator OnPlayerPickStart(IGameModeHandler gm)
        {
            FlushBarDebt();
            if (Unbound.Instance != null)
            {
                Unbound.Instance.ExecuteAfterSeconds(0.25f, FlushBarDebt);
                Unbound.Instance.ExecuteAfterSeconds(1f, FlushBarDebt);
                Unbound.Instance.ExecuteAfterSeconds(2.5f, FlushBarDebt);
            }

            yield break;
        }

        private static IEnumerator OnPointStart(IGameModeHandler gm)
        {
            FlushBarDebt();
            yield break;
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
                    // Same 2s force-display as Golden Egg / Thief. 0,0 added the stats
                    // but the top-right bar is torn down between rounds and never rebuilt.
                    CardsApi.instance.AddCardToPlayer(player, card, false, "", 2f, 2f, true);
                }
            }

            RememberBarDebt(player, grants);

            var names = string.Join(", ", grants.Where(c => c != null).Select(c => c.cardName));
            Plugin.Instance?.Log(
                $"Silver Egg hatched for player {playerId} ({PlayerLabels.For(player)}) -> {grants.Count} card(s): {names}");

            if (player.data?.view != null && player.data.view.IsMine)
            {
                CardTargetUi.ShowToast(grants.Count == 0
                    ? "Silver Egg hatched, but no cards this time."
                    : "Silver Egg hatched: " + names);
            }

            EnsureHatchedCardsOnBar(player, grants);
        }

        internal static void FlushBarDebt()
        {
            if (PendingBar.Count == 0) return;

            for (var i = PendingBar.Count - 1; i >= 0; i--)
            {
                var debt = PendingBar[i];
                var player = PickPhase.FindPlayer(debt.PlayerId);
                if (player == null || debt.Card == null)
                {
                    PendingBar.RemoveAt(i);
                    continue;
                }

                try
                {
                    EnsureOneOnBar(player, debt.Card);
                }
                catch (Exception ex)
                {
                    Plugin.Instance?.LogWarn($"Silver Egg bar flush skipped: {ex.Message}");
                    continue;
                }

                var bar = CardBarUtils.instance?.PlayersCardBar(player.playerID);
                if (bar != null && CountOnBar(bar, debt.Card) >= CountOwnedNamed(player, debt.Card)
                    && CountOwnedNamed(player, debt.Card) > 0)
                {
                    PendingBar.RemoveAt(i);
                }
            }

            try
            {
                CardBarMiniIcons.RestampAll();
            }
            catch (Exception ex)
            {
                Plugin.Instance?.LogWarn($"Silver Egg restamp skipped: {ex.Message}");
            }
        }

        private static void RememberBarDebt(Player player, List<CardInfo> grants)
        {
            if (player == null || grants == null) return;
            foreach (var card in grants)
            {
                if (card == null) continue;
                PendingBar.Add(new BarDebt { PlayerId = player.playerID, Card = card });
            }
        }

        private static void EnsureHatchedCardsOnBar(Player player, List<CardInfo> grants)
        {
            if (player == null || grants == null || grants.Count == 0) return;

            void Pass()
            {
                try
                {
                    EnsureCardsOnBar(player, grants);
                    CardBarMiniIcons.RestampAll();
                }
                catch (Exception ex)
                {
                    Plugin.Instance?.LogWarn($"Silver Egg bar stamp skipped: {ex.Message}");
                }
            }

            Pass();
            if (Unbound.Instance == null) return;
            Unbound.Instance.ExecuteAfterFrames(2, Pass);
            Unbound.Instance.ExecuteAfterFrames(8, Pass);
            Unbound.Instance.ExecuteAfterFrames(20, Pass);
        }

        private static void EnsureCardsOnBar(Player player, List<CardInfo> grants)
        {
            foreach (var card in grants)
            {
                if (card == null) continue;
                EnsureOneOnBar(player, card);
            }
        }

        private static void EnsureOneOnBar(Player player, CardInfo card)
        {
            if (player == null || card == null) return;
            var owned = CountOwnedNamed(player, card);
            if (owned <= 0) return;

            var bar = CardBarUtils.instance?.PlayersCardBar(player.playerID);
            if (bar != null && CountOnBar(bar, card) >= owned) return;

            try
            {
                CardBarUtils.SilentAddToCardBar(player.playerID, card, "");
            }
            catch (Exception ex)
            {
                Plugin.Instance?.LogWarn($"Silver Egg SilentAdd skipped: {ex.Message}");
            }

            bar = CardBarUtils.instance?.PlayersCardBar(player.playerID);
            if (bar != null && CountOnBar(bar, card) >= owned) return;
            if (CardBarHandler.instance == null) return;
            try
            {
                CardBarHandler.instance.AddCard(player.playerID, card);
            }
            catch (Exception ex)
            {
                Plugin.Instance?.LogWarn($"Silver Egg CardBarHandler.AddCard skipped: {ex.Message}");
            }
        }

        private static int CountOwnedNamed(Player player, CardInfo card)
        {
            var cards = player?.data?.currentCards;
            if (cards == null || card == null) return 0;
            var count = 0;
            foreach (var owned in cards)
            {
                if (owned == null) continue;
                if (owned == card ||
                    string.Equals(owned.cardName, card.cardName, StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountOnBar(CardBar bar, CardInfo card)
        {
            if (bar == null || card == null) return 0;
            var count = 0;
            var field = AccessTools.Field(typeof(CardBarButton), "card");
            foreach (var button in bar.GetComponentsInChildren<CardBarButton>(true))
            {
                if (button == null) continue;
                var onBar = field?.GetValue(button) as CardInfo;
                if (onBar == null) continue;
                if (onBar == card ||
                    string.Equals(onBar.cardName, card.cardName, StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                }
            }

            return count;
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
