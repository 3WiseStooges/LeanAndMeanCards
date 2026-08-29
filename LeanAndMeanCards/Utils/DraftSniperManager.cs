using System.Collections.Generic;
using LeanAndMeanCards.Cards;
using Photon.Pun;
using UnboundLib;
using UnboundLib.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace LeanAndMeanCards.Utils
{
    internal static class DraftSniperManager
    {
        private static readonly HashSet<int> BlockedViews = new HashSet<int>();
        private static readonly Dictionary<int, int> UsesConsumed = new Dictionary<int, int>();

        private static float _clickLockUntil;
        private static int _hintHandKey = int.MinValue;
        private static float _lockedClickToastUntil;

        internal static void ResetForNewGame()
        {
            UsesConsumed.Clear();
            ResetForPick();
        }

        /// <summary>
        /// Photon ViewIDs reuse across picks. Stale BlockedViews make DraftSniperPickPatch
        /// skip Pick forever (online softlock). Flush locks every pick end.
        /// </summary>
        internal static void ResetForPick()
        {
            BlockedViews.Clear();
            _clickLockUntil = 0f;
            _hintHandKey = int.MinValue;
            _lockedClickToastUntil = 0f;
            DraftSniperLockUi.Clear();
        }

        internal static int CountOwned(Player player)
        {
            var cards = player?.data?.currentCards;
            if (cards == null) return 0;
            var count = 0;
            foreach (var card in cards)
            {
                if (card == null) continue;
                if (DraftSniper.Card != null && card == DraftSniper.Card) count++;
                else if (string.Equals(card.cardName, DraftSniper.Title, System.StringComparison.OrdinalIgnoreCase)) count++;
            }

            return count;
        }

        internal static int Remaining(Player player)
        {
            if (player == null) return 0;
            UsesConsumed.TryGetValue(player.playerID, out var used);
            return Mathf.Max(0, CountOwned(player) - used);
        }

        /// <summary>
        /// True when this specific offered card is locked.
        ///
        /// A null card is NOT a locked card. It returns true only because callers use this
        /// to mean "unsafe to act on", and every caller must null-check first. Never use it
        /// to gate CardChoice.Pick, whose null argument means "build the offer".
        /// </summary>
        internal static bool IsBlocked(GameObject card)
        {
            if (card == null) return true;
            var view = card.GetComponent<PhotonView>();
            return view != null && BlockedViews.Contains(view.ViewID);
        }

        internal static void NotifyLockedClick()
        {
            var picker = PickPhase.GetCurrentPicker();
            if (picker == null || !LocalPlayerUtil.IsLocallyControlled(picker)) return;
            if (Time.unscaledTime < _lockedClickToastUntil) return;
            _lockedClickToastUntil = Time.unscaledTime + 1.2f;
            CardTargetUi.ShowToast("Draft Sniper locked this card — pick another.");
        }

        internal static void NotifyGained(Player player)
        {
            if (player?.data?.view == null || !player.data.view.IsMine) return;
            var left = Remaining(player);
            if (left <= 0) return;

            var extra = left == 1
                ? "During someone else's pick, hit the LOCK button under a card."
                : $"Stacked. {left} locks ready — hit LOCK under a card during someone else's pick.";
            PlayerNotice.Show(player, extra);
        }

        internal static void Tick()
        {
            if (CardChoice.instance == null || !CardChoice.instance.IsPicking)
            {
                _hintHandKey = int.MinValue;
                DraftSniperLockUi.Clear();
                return;
            }

            var local = LocalPlayerUtil.LocalPlayer();
            if (!CanLocalSnipe(local))
            {
                DraftSniperLockUi.Clear();
                return;
            }

            var offered = PickPhase.GetOfferedCards();
            DraftSniperLockUi.Sync(offered, Remaining(local));
            MaybeHint(local, offered);
        }

        /// <summary>
        /// Raised by a card's own LOCK button. Returns true when the request went out, so
        /// the button knows to show its pending state.
        ///
        /// The conditions are re-checked here rather than trusted from the button: a hand
        /// can be picked, replaced or rerolled between the frame that drew the button and
        /// the frame the click lands on.
        /// </summary>
        internal static bool TryLock(GameObject card)
        {
            if (card == null || IsBlocked(card)) return false;
            if (Time.unscaledTime < _clickLockUntil) return false;

            var local = LocalPlayerUtil.LocalPlayer();
            if (!CanLocalSnipe(local)) return false;

            var view = card.GetComponent<PhotonView>();
            if (view == null || view.ViewID == 0) return false;

            _clickLockUntil = Time.unscaledTime + 0.35f;
            NetworkingManager.RPC(typeof(DraftSniperManager), nameof(RPCA_BanCard), local.playerID, view.ViewID);
            return true;
        }

        [UnboundRPC]
        public static void RPCA_BanCard(int sniperId, int viewId)
        {
            if (!(PhotonNetwork.OfflineMode || PhotonNetwork.IsMasterClient)) return;
            if (!IsValidSnipe(sniperId))
            {
                NetworkingManager.RPC(typeof(DraftSniperManager), nameof(RPCA_LockFailed), sniperId, "Can't lock that card.");
                return;
            }

            if (BlockedViews.Contains(viewId))
            {
                NetworkingManager.RPC(typeof(DraftSniperManager), nameof(RPCA_LockFailed), sniperId, "That card is already locked.");
                return;
            }

            var view = PhotonView.Find(viewId);
            var card = view != null ? view.gameObject : null;
            if (card == null || !IsInOfferedHand(card))
            {
                NetworkingManager.RPC(typeof(DraftSniperManager), nameof(RPCA_LockFailed), sniperId, "That card is gone.");
                return;
            }

            if (UnlockedReadyCount() <= 1)
            {
                NetworkingManager.RPC(typeof(DraftSniperManager), nameof(RPCA_LockFailed), sniperId, "Can't lock the last card.");
                return;
            }

            var picker = PickPhase.GetCurrentPicker();
            var sniper = FindPlayer(sniperId);
            var source = PickPhase.SourceOf(card);
            var cardName = source != null && !string.IsNullOrEmpty(source.cardName) ? source.cardName : "a card";

            // Record on the host before broadcasting so a second BanCard in the
            // same frame cannot lock the last remaining card.
            BlockedViews.Add(viewId);
            ApplyLockVisual(viewId);
            NetworkingManager.RPC(typeof(DraftSniperManager), nameof(RPCA_Lock), viewId);
            NetworkingManager.RPC(typeof(DraftSniperManager), nameof(RPCA_ConsumeUse), sniperId);
            NetworkingManager.RPC(
                typeof(DraftSniperManager),
                nameof(RPCA_AnnounceBan),
                PlayerLabel(sniper, sniperId),
                cardName,
                PlayerLabel(picker, picker != null ? picker.playerID : -1));
            Plugin.Instance?.Log(
                $"Draft Sniper lock view={viewId} card={cardName} sniper={sniperId} " +
                $"picker={(picker != null ? picker.playerID : -1)} unlockedLeft={UnlockedReadyCount()}");
        }

        [UnboundRPC]
        public static void RPCA_Lock(int viewId)
        {
            BlockedViews.Add(viewId);
            ApplyLockVisual(viewId);
        }

        [UnboundRPC]
        public static void RPCA_LockFailed(int sniperId, string reason)
        {
            var local = LocalPlayerUtil.LocalPlayer();
            if (local == null || local.playerID != sniperId) return;
            if (string.IsNullOrEmpty(reason)) return;
            CardTargetUi.ShowToast(reason);
        }

        [UnboundRPC]
        public static void RPCA_ConsumeUse(int sniperId)
        {
            UsesConsumed.TryGetValue(sniperId, out var used);
            UsesConsumed[sniperId] = used + 1;
        }

        [UnboundRPC]
        public static void RPCA_AnnounceBan(string sniperName, string cardName, string targetName)
        {
            CardTargetUi.ShowToast($"{sniperName} locked {cardName} from {targetName}.");
        }

        private static bool CanLocalSnipe(Player local)
        {
            if (local == null || Remaining(local) <= 0) return false;
            if (ExternalPickState.IsBulkCollecting) return false;
            if (!PickPhase.IsOfferedHandReady()) return false;
            var picker = PickPhase.GetCurrentPicker();
            if (picker == null) return false;
            if (SameTeam(local, picker)) return false;
            return !LocalPlayerUtil.IsLocallyControlled(picker);
        }

        private static bool IsValidSnipe(int sniperId)
        {
            var sniper = FindPlayer(sniperId);
            if (sniper == null || Remaining(sniper) <= 0) return false;
            if (ExternalPickState.IsBulkCollecting) return false;
            if (!PickPhase.IsOfferedHandReady()) return false;
            var picker = PickPhase.GetCurrentPicker();
            if (picker == null || picker.playerID == sniperId) return false;
            return !SameTeam(sniper, picker);
        }

        private static bool SameTeam(Player a, Player b)
        {
            return a != null && b != null && a.teamID == b.teamID;
        }

        /// <summary>
        /// Host-side check, so it must not read spawnedCards: the host is only the picker
        /// some of the time, and on every other client that list is empty until the pick is
        /// already over. Validating a lock against it rejected every lock in the lobby with
        /// "That card is gone."
        /// </summary>
        private static bool IsInOfferedHand(GameObject card)
        {
            if (card == null) return false;
            var offered = PickPhase.GetOfferedCards();
            return offered != null && offered.Contains(card);
        }

        private static int UnlockedReadyCount()
        {
            var spawned = PickPhase.GetOfferedCards();
            if (spawned == null) return 0;
            var n = 0;
            foreach (var go in spawned)
            {
                if (go == null || IsBlocked(go)) continue;
                n++;
            }

            return n;
        }

        private static void ApplyLockVisual(int viewId)
        {
            try
            {
                var view = PhotonView.Find(viewId);
                var card = view != null ? view.gameObject : null;
                if (card == null) return;
                if (card.GetComponentInChildren<DraftSniperLockMark>(true) != null) return;

                var parent = card.GetComponentInChildren<Canvas>(true);
                var root = parent != null ? parent.transform : card.transform;
                var go = new GameObject("MM_DraftSniperLock", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                go.transform.SetParent(root, false);
                go.transform.SetAsLastSibling();
                go.AddComponent<DraftSniperLockMark>();

                var rect = go.GetComponent<RectTransform>();
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                rect.localScale = Vector3.one;

                var img = go.GetComponent<Image>();
                img.color = new Color(0.04f, 0.04f, 0.06f, 0.78f);
                img.raycastTarget = false;
            }
            catch
            {
                // Overlay is cosmetic; lock still blocks Pick.
            }
        }

        private static void MaybeHint(Player local, List<GameObject> spawned)
        {
            if (spawned == null || spawned.Count == 0) return;
            var key = spawned.Count;
            foreach (var go in spawned)
            {
                if (go != null) key ^= go.GetInstanceID();
            }

            if (key == _hintHandKey) return;
            _hintHandKey = key;
            var left = Remaining(local);
            CardTargetUi.ShowToast(left == 1
                ? "Draft Sniper: hit LOCK under a card to take it off the table."
                : $"Draft Sniper: hit LOCK under a card to take it off the table ({left} left).");
        }

        private static Player FindPlayer(int playerId)
        {
            if (PlayerManager.instance?.players == null) return null;
            foreach (var player in PlayerManager.instance.players)
            {
                if (player != null && player.playerID == playerId) return player;
            }

            return null;
        }

        private static string PlayerLabel(Player player, int id)
        {
            var name = player?.data?.view?.Owner?.NickName;
            return string.IsNullOrEmpty(name) ? "Player " + (id + 1) : name;
        }
    }

    internal sealed class DraftSniperLockMark : MonoBehaviour
    {
    }

    internal sealed class DraftSniperTicker : MonoBehaviour
    {
        private void Update() => DraftSniperManager.Tick();
    }
}
