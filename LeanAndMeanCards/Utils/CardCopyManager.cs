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
    /// The chooser runs at pick end, not during the pick, so it never lands on top of
    /// somebody else's picking turn. Same shape as Genie's wish shop: the pick-end hook
    /// blocks on every client for up to two minutes while whoever is owed a copy chooses,
    /// everyone else gets a waiting banner with the same countdown, and the clock running
    /// out resolves the choice rather than leaving the round hanging.
    ///
    /// Who does what: the owner's client runs the chooser and asks, the master validates
    /// and performs the add (ModdingUtils' AddCardToPlayer is master-only and RPCs the
    /// result to everyone), and every client keeps the ledger so Clone keeps ticking even
    /// if the host changes mid-game.
    ///
    /// Nothing here is a queue. How much a player is owed is derived from live state —
    /// Duplicates held minus Duplicates spent, Clones held minus Clones registered — which
    /// is also what makes the wait loop safe: every client computes "is anyone still
    /// choosing" from the same synced state, with no handshake to race.
    /// </summary>
    internal static class CardCopyManager
    {
        /// <summary>Genie's wish shop uses the same two minutes.</summary>
        internal const float ChooseSeconds = 120f;

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
            GameModeManager.AddHook(GameModeHooks.HookPickEnd, OnPickEnd);
            GameModeManager.AddHook(GameModeHooks.HookPointStart, OnPointStart);
        }

        internal static void ResetForNewGame()
        {
            Clones.Clear();
            DuplicatesSpent.Clear();
            CardBarDebt.Reset();
            CardCopyUi.HideWaiting();
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

        /// <summary>
        /// Owed a copy *and* holding something copyable. The second half matters: an
        /// unspendable Duplicate would otherwise stall every pick end for a full two
        /// minutes waiting on a choice that cannot be made.
        /// </summary>
        private static bool OwesCopy(Player player)
        {
            if (player == null) return false;
            if (DuplicateDebt(player) <= 0 && CloneDebt(player) <= 0) return false;
            return CardCopyRules.CountCopyable(player) > 0;
        }

        /// <summary>
        /// Is there a person who can answer a chooser for this player? Offline that is
        /// only the local human — a bot has nobody, and waiting on one would hang the
        /// round for the full two minutes every time an AI picked Duplicate.
        /// </summary>
        private static bool IsHumanControlled(Player player)
        {
            if (player?.data == null) return false;
            if (!PhotonNetwork.OfflineMode) return true;

            var local = LocalPlayerUtil.LocalPlayer();
            return local != null && local.playerID == player.playerID;
        }

        /// <summary>The players the lobby actually waits for at pick end.</summary>
        private static List<Player> PlayersChoosing()
        {
            var result = new List<Player>();
            var players = PlayerManager.instance?.players;
            if (players == null) return result;

            foreach (var player in players)
            {
                if (OwesCopy(player) && IsHumanControlled(player)) result.Add(player);
            }

            return result;
        }

        // ------------------------------------------------------------- prompting

        /// <summary>
        /// Called from OnAddCard. Deliberately does not open anything: the chooser waits
        /// for pick end so it cannot overlap another player's turn. This only tells the
        /// owner that something is coming.
        /// </summary>
        internal static void NotifyCopyCardGained(Player player, CardCopyKind kind)
        {
            if (!IsLocalOwner(player)) return;

            PlayerNotice.Show(player, kind == CardCopyKind.Clone
                ? "Clone: you choose a card once everyone has picked."
                : "Duplicate: you choose a card once everyone has picked.");
        }

        /// <summary>Opens the chooser for the local player, if this client is owed a copy.</summary>
        private static void TryPromptLocal()
        {
            if (CardCopyUi.IsOpen || StealUi.IsOpen || CardTargetUi.IsOpen) return;

            var players = PlayerManager.instance?.players;
            if (players == null) return;

            foreach (var player in players)
            {
                if (!IsLocalOwner(player) || !OwesCopy(player)) continue;

                // Clone first: it is the bigger effect and it starts paying out this round.
                var kind = CloneDebt(player) > 0 ? CardCopyKind.Clone : CardCopyKind.Duplicate;
                if (CardCopyUi.TryOpen(player, kind)) return;
            }
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

        /// <summary>
        /// The whole pick phase is over here, so a chooser can open without stepping on
        /// anyone's turn. Every client runs this and blocks together: the "still choosing"
        /// test reads synced state, so no client needs to be told when to stop waiting.
        /// </summary>
        private static IEnumerator OnPickEnd(IGameModeHandler gm)
        {
            ReconcileSpent();
            CardBarDebt.Flush();

            // Bots get theirs resolved on the spot. Nobody can answer a chooser for them,
            // and the alternative is either a dead card or a two-minute hang.
            if (PhotonNetwork.OfflineMode || PhotonNetwork.IsMasterClient) AutoResolve(forHumans: false);

            if (PlayersChoosing().Count == 0) yield break;

            TryPromptLocal();

            var left = ChooseSeconds;
            var recheck = 0f;
            var waiting = PlayersChoosing();

            while (left > 0f && waiting.Count > 0)
            {
                // Unscaled: the between-rounds screen does not run on game time.
                left -= Time.unscaledDeltaTime;
                recheck -= Time.unscaledDeltaTime;

                // Four times a second, not every frame: the copyable test runs a
                // reflection call per card per player, and this loop can run for two
                // minutes. The countdown still updates every frame.
                if (recheck <= 0f)
                {
                    recheck = 0.25f;
                    waiting = PlayersChoosing();
                    if (waiting.Count == 0) break;

                    // Retry: this client's chooser may have been blocked by Thief's
                    // overlay when the hook started, and that can close while we wait.
                    TryPromptLocal();
                }

                if (CardCopyUi.IsOpen)
                {
                    CardCopyUi.HideWaiting();
                    CardCopyUi.SetCountdown(left);
                }
                else
                {
                    // Someone else is choosing, or this client's chooser could not open.
                    CardCopyUi.ShowWaiting(WaitingText(waiting), left);
                }

                yield return null;
            }

            CardCopyUi.Close();
            CardCopyUi.HideWaiting();

            // Out of time. Resolve rather than leave it pending, or an away player stalls
            // the lobby for two minutes again every single pick end.
            if (PhotonNetwork.OfflineMode || PhotonNetwork.IsMasterClient) AutoResolve(forHumans: true);
        }

        private static string WaitingText(List<Player> waiting)
        {
            if (waiting.Count == 1)
            {
                return $"Waiting for {PlayerLabels.For(waiting[0])} to copy a card";
            }

            return $"Waiting for {waiting.Count} players to copy a card";
        }

        /// <summary>
        /// Master-side fallback: spend an outstanding copy on the best card that player
        /// holds, so it is never wasted and the round always moves on. Used for bots up
        /// front, and for a human who let the clock run out.
        /// </summary>
        private static void AutoResolve(bool forHumans)
        {
            var players = PlayerManager.instance?.players;
            if (players == null) return;

            foreach (var player in players)
            {
                if (!OwesCopy(player) || IsHumanControlled(player) != forHumans) continue;

                var card = BestCopyable(player);
                if (card == null) continue;

                var clone = CloneDebt(player) > 0;
                Plugin.Instance?.Log(
                    $"{(clone ? Clone.Title : Duplicate.Title)}: timed out for player {player.playerID} "
                    + $"({PlayerLabels.For(player)}), auto-picking '{card.cardName}'.");

                NetworkingManager.RPC(
                    typeof(CardCopyManager),
                    nameof(RPCA_ApplyCopy),
                    player.playerID,
                    CardEncoding.Encode(card),
                    clone);
            }
        }

        private static CardInfo BestCopyable(Player player)
        {
            var cards = player?.data?.currentCards;
            if (cards == null) return null;

            CardInfo best = null;
            foreach (var card in cards)
            {
                if (card == null) continue;
                if (!CardCopyRules.IsCopyable(player, card, out _)) continue;
                if (best == null || (int)card.rarity > (int)best.rarity) best = card;
            }

            return best;
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
