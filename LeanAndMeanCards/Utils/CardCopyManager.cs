using System;
using System.Collections;
using System.Collections.Generic;
using LeanAndMeanCards.Cards;
using Photon.Pun;
using UnboundLib;
using UnboundLib.GameModes;
using UnboundLib.Networking;
using UnityEngine;
using CardsApi = ModdingUtils.Utils.Cards;

namespace LeanAndMeanCards.Utils
{
    /// <summary>
    /// Duplicate and Clone: copying a card out of your own hand.
    ///
    /// Who does what, because getting this wrong is how pick-phase bugs happen:
    /// the owner's client runs the chooser and asks, the master client validates and
    /// performs the add (ModdingUtils' AddCardToPlayer is master-only and RPCs the result
    /// to everyone), and every client keeps the ledger so Clone keeps ticking even if the
    /// host changes mid-game.
    ///
    /// Nothing here is a queue. How much a player is owed is derived from live state —
    /// Duplicates held minus Duplicates spent, Clones held minus Clones registered — so a
    /// double ApplyStats (Simulacrum) or a closed chooser cannot leave a phantom debt
    /// behind, and the prompt simply comes back at the next pick.
    /// </summary>
    internal static class CardCopyManager
    {
        // Must NOT be readonly structs: Unity Mono lacks IsReadOnlyAttribute and Harmony
        // aborts the whole assembly's patching if it sees one.
        private sealed class CloneReg
        {
            public int PlayerId;
            public string Payload;
            public string CardName;
        }

        private static readonly List<CloneReg> Clones = new List<CloneReg>();
        private static readonly Dictionary<int, int> DuplicatesSpent = new Dictionary<int, int>();

        internal static void RegisterHooks()
        {
            GameModeManager.AddHook(GameModeHooks.HookGameStart, OnGameStart);
            GameModeManager.AddHook(GameModeHooks.HookRoundEnd, OnRoundEnd);
            GameModeManager.AddHook(GameModeHooks.HookPickStart, OnPickStart);
            GameModeManager.AddHook(GameModeHooks.HookPointStart, OnPointStart);
        }

        internal static void ResetForNewGame()
        {
            Clones.Clear();
            DuplicatesSpent.Clear();
            CardBarDebt.Reset();
        }

        // ---------------------------------------------------------------- debts

        internal static int DuplicateDebt(Player player)
        {
            if (player == null) return 0;
            var owned = CountOwned(player, Duplicate.Card, Duplicate.Title);
            return Mathf.Max(0, owned - Spent(player.playerID));
        }

        internal static int CloneDebt(Player player)
        {
            if (player == null) return 0;
            var owned = CountOwned(player, Clone.Card, Clone.Title);
            return Mathf.Max(0, owned - CountRegistrations(player.playerID));
        }

        private static int Spent(int playerId) =>
            DuplicatesSpent.TryGetValue(playerId, out var n) ? n : 0;

        private static int CountRegistrations(int playerId)
        {
            var count = 0;
            foreach (var reg in Clones)
            {
                if (reg.PlayerId == playerId) count++;
            }

            return count;
        }

        // ------------------------------------------------------------- prompting

        /// <summary>
        /// Called from OnAddCard for the card that was just picked.
        ///
        /// Two things are deliberate. The chooser opens synchronously, like Thief's: it
        /// takes the pick hold while the pick is still running, and a delayed open could
        /// push that hold after the pick-end hook had already stopped waiting, which drops
        /// the whole lobby into the round underneath the overlay. And it does not consult
        /// the debt, because UnboundLib runs OnAddCard as a *prefix* on ApplyStats — the
        /// new card is not in currentCards yet, so every count here is one short. The
        /// master re-checks the debt when the request arrives, seconds later, by which
        /// point the hand is settled on every client.
        /// </summary>
        internal static void NotifyCopyCardGained(Player player, CardCopyKind kind)
        {
            if (!IsLocalOwner(player)) return;

            if (CardCopyRules.CountCopyable(player) <= 0)
            {
                // Nothing worth copying yet (Duplicate as a first card, a hand of curses).
                // The card stays unspent, so the chooser comes back at the next pick.
                PlayerNotice.Show(player, "Nothing in your hand can be copied yet.");
                return;
            }

            CardCopyUi.TryOpen(player, kind);
        }

        /// <summary>
        /// Re-opens the chooser for a copy card that is still unspent: closed with "Later",
        /// or picked when nothing in the hand could be copied yet.
        ///
        /// Only on the local player's own pick, like Thief's deferred prompt. The chooser
        /// holds the whole lobby while it is up, so opening it during somebody else's pick
        /// would stall a player who has nothing to do with it.
        /// </summary>
        internal static void TryPromptLocal()
        {
            if (CardCopyUi.IsOpen || StealUi.IsOpen || CardTargetUi.IsOpen) return;

            var picker = PickPhase.GetCurrentPicker();
            if (!IsLocalOwner(picker)) return;

            var wantsClone = CloneDebt(picker) > 0;
            var wantsDuplicate = DuplicateDebt(picker) > 0;
            if (!wantsClone && !wantsDuplicate) return;

            if (CardCopyRules.CountCopyable(picker) <= 0)
            {
                PlayerNotice.Show(picker, "Nothing in your hand can be copied yet.");
                return;
            }

            // Clone first: it is the bigger effect and it starts paying out this round.
            CardCopyUi.TryOpen(picker, wantsClone ? CardCopyKind.Clone : CardCopyKind.Duplicate);
        }

        // -------------------------------------------------------------- requests

        internal static void Request(Player owner, CardInfo card, CardCopyKind kind)
        {
            if (owner == null || card == null) return;

            if (!CardCopyRules.IsCopyable(owner, card, out var reason))
            {
                PlayerNotice.Show(owner, $"Can't copy {card.cardName}: {reason}.");
                return;
            }

            var payload = CardEncoding.Encode(card);
            if (CardEncoding.FindCardIndex(owner, payload) < 0)
            {
                PlayerNotice.Show(owner, "That card is gone.");
                return;
            }

            NetworkingManager.RPC(
                typeof(CardCopyManager),
                nameof(RPCA_RequestCopy),
                owner.playerID,
                payload,
                kind == CardCopyKind.Clone);
        }

        [UnboundRPC]
        public static void RPCA_RequestCopy(int playerId, string payload, bool clone)
        {
            if (!(PhotonNetwork.OfflineMode || PhotonNetwork.IsMasterClient)) return;

            var player = PickPhase.FindPlayer(playerId);
            var card = CardEncoding.Resolve(payload);
            if (player == null || card == null)
            {
                Plugin.Instance?.LogWarn($"Copy request failed - player or card missing (player {playerId}).");
                return;
            }

            if (!CardCopyRules.IsCopyable(player, card, out var reason))
            {
                Plugin.Instance?.LogWarn($"Copy request rejected for player {playerId}: {reason}.");
                return;
            }

            // The owner's client already checked this, but the master decides. Without it a
            // stale or replayed request could spend a copy the player no longer holds.
            var debt = clone ? CloneDebt(player) : DuplicateDebt(player);
            if (debt <= 0)
            {
                Plugin.Instance?.LogWarn(
                    $"Copy request rejected for player {playerId}: no unspent {(clone ? Clone.Title : Duplicate.Title)}.");
                return;
            }

            NetworkingManager.RPC(typeof(CardCopyManager), nameof(RPCA_ApplyCopy), playerId, payload, clone);
        }

        [UnboundRPC]
        public static void RPCA_ApplyCopy(int playerId, string payload, bool clone)
        {
            var player = PickPhase.FindPlayer(playerId);
            var card = CardEncoding.Resolve(payload);
            if (player == null || card == null) return;

            if (clone)
            {
                Clones.Add(new CloneReg { PlayerId = playerId, Payload = payload, CardName = card.cardName });
            }
            else
            {
                DuplicatesSpent[playerId] = Spent(playerId) + 1;
            }

            GrantCopy(player, card);

            PlayerNotice.Show(player, clone
                ? $"Clone locked onto {card.cardName}. Another copy every round."
                : $"Duplicated {card.cardName}.");

            Plugin.Instance?.Log(
                $"{(clone ? Clone.Title : Duplicate.Title)}: player {playerId} ({PlayerLabels.For(player)}) copied '{card.cardName}'.");
        }

        // ------------------------------------------------------------ round tick

        [UnboundRPC]
        public static void RPCA_CloneTick(int playerId, string payload)
        {
            var player = PickPhase.FindPlayer(playerId);
            var card = CardEncoding.Resolve(payload);
            if (player == null || card == null) return;

            GrantCopy(player, card);
            PlayerNotice.Show(player, $"Clone: another {card.cardName}.");
            Plugin.Instance?.Log(
                $"Clone tick: player {playerId} ({PlayerLabels.For(player)}) gained another '{card.cardName}'.");
        }

        /// <summary>
        /// Master-only add, bar reconciliation everywhere. AddCardToPlayer RPCs the real
        /// assignment to every client itself, so calling it on more than one peer would
        /// hand out the card several times over.
        /// </summary>
        private static void GrantCopy(Player player, CardInfo card)
        {
            if (player == null || card == null) return;

            if (PhotonNetwork.OfflineMode || PhotonNetwork.IsMasterClient)
            {
                try
                {
                    // Same 2s force-display as Thief and the eggs.
                    CardsApi.instance.AddCardToPlayer(player, card, false, "", 2f, 2f, true);
                }
                catch (Exception ex)
                {
                    Plugin.Instance?.LogWarn($"Copy add failed for '{card.cardName}': {ex.Message}");
                    return;
                }
            }

            CardBarDebt.Remember(player, card);
            CardBarDebt.StampSoon(player, card);
        }

        /// <summary>
        /// A Clone that left the hand stops copying. Runs on every client off the same
        /// synced currentCards, so the ledgers do not drift.
        /// </summary>
        private static void PruneLostClones()
        {
            if (Clones.Count == 0) return;

            var counted = new Dictionary<int, int>();
            for (var i = Clones.Count - 1; i >= 0; i--)
            {
                var reg = Clones[i];
                var player = PickPhase.FindPlayer(reg.PlayerId);
                if (player == null)
                {
                    Clones.RemoveAt(i);
                    continue;
                }

                var owned = CountOwned(player, Clone.Card, Clone.Title);
                counted.TryGetValue(reg.PlayerId, out var kept);
                if (kept >= owned)
                {
                    Plugin.Instance?.Log(
                        $"Clone on '{reg.CardName}' dropped for player {reg.PlayerId} - the card is gone.");
                    Clones.RemoveAt(i);
                    continue;
                }

                counted[reg.PlayerId] = kept + 1;
            }
        }

        // ----------------------------------------------------------------- hooks

        private static IEnumerator OnGameStart(IGameModeHandler gm)
        {
            ResetForNewGame();
            yield break;
        }

        private static IEnumerator OnRoundEnd(IGameModeHandler gm)
        {
            yield return null;

            PruneLostClones();
            if (!(PhotonNetwork.OfflineMode || PhotonNetwork.IsMasterClient)) yield break;

            // Snapshot first: the grants below add cards, and a Clone must not react to
            // what this same tick handed out.
            var snapshot = Clones.ToArray();
            foreach (var reg in snapshot)
            {
                if (reg == null || string.IsNullOrEmpty(reg.Payload)) continue;
                NetworkingManager.RPC(typeof(CardCopyManager), nameof(RPCA_CloneTick), reg.PlayerId, reg.Payload);
            }
        }

        private static IEnumerator OnPickStart(IGameModeHandler gm)
        {
            CardBarDebt.Flush();
            yield break;
        }

        private static IEnumerator OnPointStart(IGameModeHandler gm)
        {
            CardBarDebt.Flush();
            yield break;
        }

        /// <summary>Called from Plugin's player-pick-start hook.</summary>
        internal static void OnPlayerPickStart()
        {
            CardBarDebt.Flush();
            ReconcileSpent();
            TryPromptLocal();

            if (Unbound.Instance == null) return;
            Unbound.Instance.ExecuteAfterSeconds(0.25f, CardBarDebt.Flush);
            Unbound.Instance.ExecuteAfterSeconds(1f, CardBarDebt.Flush);
            Unbound.Instance.ExecuteAfterSeconds(2.5f, CardBarDebt.Flush);
        }

        /// <summary>
        /// Spent counts never exceed Duplicates held. Without this, losing a spent
        /// Duplicate (Null conversion, a card wipe) and later picking a fresh one would
        /// leave the new card inert.
        /// </summary>
        private static void ReconcileSpent()
        {
            if (DuplicatesSpent.Count == 0) return;

            var ids = new List<int>(DuplicatesSpent.Keys);
            foreach (var id in ids)
            {
                var player = PickPhase.FindPlayer(id);
                var owned = player == null ? 0 : CountOwned(player, Duplicate.Card, Duplicate.Title);
                if (DuplicatesSpent[id] > owned) DuplicatesSpent[id] = owned;
            }
        }

        // ---------------------------------------------------------------- status

        internal static bool ShowDuplicateStat(Player player) => DuplicateDebt(player) > 0;

        internal static string DuplicateStatusText(Player player)
        {
            var debt = DuplicateDebt(player);
            if (debt <= 0) return "";
            return debt == 1 ? "a copy to spend" : debt + " copies to spend";
        }

        internal static bool ShowCloneStat(Player player) =>
            player != null && (CountRegistrations(player.playerID) > 0 || CloneDebt(player) > 0);

        internal static string CloneStatusText(Player player)
        {
            if (player == null) return "";

            var names = new List<string>();
            foreach (var reg in Clones)
            {
                if (reg.PlayerId != player.playerID) continue;
                if (!string.IsNullOrEmpty(reg.CardName)) names.Add(reg.CardName);
            }

            if (names.Count > 0) return "copying " + string.Join(", ", names.ToArray());
            return CloneDebt(player) > 0 ? "pick a card to copy" : "";
        }

        // ---------------------------------------------------------------- shared

        private static int CountOwned(Player player, CardInfo card, string title)
        {
            var cards = player?.data?.currentCards;
            if (cards == null) return 0;

            var count = 0;
            foreach (var owned in cards)
            {
                if (owned == null) continue;
                if (card != null && owned == card) count++;
                else if (string.Equals(owned.cardName, title, StringComparison.OrdinalIgnoreCase)) count++;
            }

            return count;
        }

        /// <summary>
        /// Only the human in front of this client may be prompted. PhotonView.IsMine is
        /// true for every player in offline mode, so a bot picking Duplicate would
        /// otherwise pop a chooser over the host's own hand.
        /// </summary>
        private static bool IsLocalOwner(Player player)
        {
            if (player?.data == null) return false;

            if (PhotonNetwork.OfflineMode)
            {
                var local = LocalPlayerUtil.LocalPlayer();
                return local != null && local.playerID == player.playerID;
            }

            return player.data.view != null && player.data.view.IsMine;
        }
    }
}
