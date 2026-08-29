using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace LeanAndMeanCards.Utils
{
    /// <summary>
    /// Read-only view of the current pick.
    ///
    /// This class never writes to CardChoice. The game already owns card spawning —
    /// CardChoice.Pick starts ReplaceCards only when the *picker's* view IsMine, and
    /// SpawnUniqueCard uses PhotonNetwork.Instantiate so every client receives the hand.
    /// Overriding that ownership (e.g. re-gating it on IsMasterClient) leaves the picker
    /// with an empty local spawnedCards list, which breaks DoPlayerSelect outright.
    /// </summary>
    internal static class PickPhase
    {
        private static readonly FieldInfo PickerTypeField = AccessTools.Field(typeof(CardChoice), "pickerType");
        private static readonly FieldInfo SpawnedCardsField = AccessTools.Field(typeof(CardChoice), "spawnedCards");
        private static readonly FieldInfo ChildrenField = AccessTools.Field(typeof(CardChoice), "children");

        private static readonly List<GameObject> Discovered = new List<GameObject>();

        private static int _actingPickerId = -1;
        private static int _lastSpawnCount;
        private static float _spawnStableSince;
        private static float _nextDiscoverScan;

        internal static void NoteActingPicker(int pickerId) => _actingPickerId = pickerId;

        internal static void ClearActingPicker()
        {
            _actingPickerId = -1;
            _lastSpawnCount = 0;
            _spawnStableSince = 0f;
            _nextDiscoverScan = 0f;
            Discovered.Clear();
        }

        internal static Player GetCurrentPicker()
        {
            var choice = CardChoice.instance;
            if (choice == null) return null;

            // RWF / Unbound TDM calls StartPick(playerID) per player even when pickerType
            // stays Team. Prefer that acting player so card effects bind to the player who
            // is actually picking instead of the lowest id on the team.
            if (choice.IsPicking && _actingPickerId >= 0)
            {
                var acting = FindPlayer(_actingPickerId);
                if (acting != null) return acting;
            }

            var pickerType = PickerTypeField != null
                ? (PickerType)PickerTypeField.GetValue(choice)
                : PickerType.Player;

            if (pickerType == PickerType.Team)
            {
                var team = PlayerManager.instance != null
                    ? PlayerManager.instance.GetPlayersInTeam(choice.pickrID)
                    : null;
                if (team != null && team.Length > 0) return DesignateFromTeam(team);
            }

            return FindPlayer(choice.pickrID);
        }

        internal static Player FindPlayer(int playerId)
        {
            var players = PlayerManager.instance?.players;
            if (players == null) return null;
            foreach (var player in players)
            {
                if (player != null && player.playerID == playerId) return player;
            }

            return null;
        }

        internal static List<GameObject> GetSpawnedCards()
        {
            var choice = CardChoice.instance;
            if (choice == null) return null;
            return SpawnedCardsField?.GetValue(choice) as List<GameObject>;
        }

        /// <summary>
        /// The cards currently on the table, on any client.
        ///
        /// CardChoice.spawnedCards is NOT a view of the offer for most of the lobby. Only the
        /// picker fills it, in ReplaceCards, because Pick gates that call on the picker's own
        /// view being IsMine. Every other client's list stays empty until RPCA_DoEndPick
        /// back-fills it from CardIDs() - and that RPC fires at the instant a card is chosen.
        /// So anything driven off spawnedCards is, for a spectator, blank for the whole pick
        /// and populated the moment the pick is over.
        ///
        /// The card objects themselves are network-wide the entire time: SpawnUniqueCard goes
        /// through PhotonNetwork.Instantiate, at CardChoice's own child anchors. That is what
        /// this reads instead, so spectators see the live offer while it is still live.
        /// </summary>
        internal static List<GameObject> GetOfferedCards()
        {
            var ready = LiveOffer(GetSpawnedCards());
            if (ready.Count > 0) return ready;
            return DiscoverOfferFromAnchors();
        }

        private static List<GameObject> LiveOffer(List<GameObject> source)
        {
            var ready = new List<GameObject>();
            if (source == null) return ready;

            foreach (var go in source)
            {
                if (!IsLiveOfferCard(go)) continue;
                ready.Add(go);
            }

            return ready;
        }

        private static bool IsLiveOfferCard(GameObject go)
        {
            if (go == null) return false;
            if (go.GetComponent<CardInfo>() == null) return false;
            var view = go.GetComponent<PhotonView>();
            if (view == null || view.ViewID == 0) return false;

            // Cards flung away by a finished pick are on their way out, not on offer.
            return go.GetComponent<RemoveAfterSeconds>() == null;
        }

        /// <summary>
        /// Finds the offer by looking at the anchors it was dealt onto. A full scene scan is
        /// too expensive for Update, so results are cached until a card dies or the interval
        /// lapses.
        /// </summary>
        private static List<GameObject> DiscoverOfferFromAnchors()
        {
            // "Nothing found" is a result worth caching too - testing for liveness instead
            // would treat the empty list as a stale one and rescan the scene every frame.
            if (Time.unscaledTime < _nextDiscoverScan && !AnyDied(Discovered))
            {
                return new List<GameObject>(Discovered);
            }

            _nextDiscoverScan = Time.unscaledTime + 0.2f;
            Discovered.Clear();

            var choice = CardChoice.instance;
            if (choice == null || !choice.IsPicking) return new List<GameObject>();

            var anchors = ChildrenField?.GetValue(choice) as Transform[];
            if (anchors == null || anchors.Length == 0) return new List<GameObject>();

            var radius = AnchorRadius(anchors);
            var candidates = Object.FindObjectsOfType<CardInfo>();

            // Anchor order is the offer's own left-to-right order, and matches the theInt
            // each card was tagged with on the picker's client.
            foreach (var anchor in anchors)
            {
                if (anchor == null) continue;

                GameObject best = null;
                var bestDistance = radius;

                foreach (var info in candidates)
                {
                    if (info == null) continue;
                    var go = info.gameObject;

                    // Card bar entries and hover previews sit under a Canvas. The test walks
                    // up from the PARENT, never the card itself - a card prefab carries its
                    // own world-space Canvas, so testing the object would reject every card
                    // on the table. Vanilla leaves offered cards as scene roots, but Pick N
                    // Cards and Pick Phase Improvements are both in this pack's usual load
                    // order, so being a root is not required - only being parked on an anchor.
                    var parent = go.transform.parent;
                    if (parent != null && parent.GetComponentInParent<Canvas>() != null) continue;
                    if (!IsLiveOfferCard(go)) continue;
                    if (Discovered.Contains(go)) continue;

                    var distance = Vector3.Distance(go.transform.position, anchor.position);
                    if (distance > bestDistance) continue;
                    bestDistance = distance;
                    best = go;
                }

                if (best != null) Discovered.Add(best);
            }

            return new List<GameObject>(Discovered);
        }

        /// <summary>
        /// Half the tightest anchor spacing, so a card can never be claimed by its neighbour's
        /// slot however the pick rig is laid out or scaled.
        /// </summary>
        private static float AnchorRadius(Transform[] anchors)
        {
            var closest = float.MaxValue;
            for (var i = 0; i < anchors.Length; i++)
            {
                if (anchors[i] == null) continue;
                for (var j = i + 1; j < anchors.Length; j++)
                {
                    if (anchors[j] == null) continue;
                    var distance = Vector3.Distance(anchors[i].position, anchors[j].position);
                    if (distance > 0.01f && distance < closest) closest = distance;
                }
            }

            return closest == float.MaxValue ? 5f : closest * 0.5f;
        }

        /// <summary>
        /// True when a card the last scan found has since been destroyed, which retires the
        /// cache early. An empty cache has nothing to lose and so is never stale.
        /// </summary>
        private static bool AnyDied(List<GameObject> cards)
        {
            foreach (var go in cards)
            {
                if (go == null) return true;
            }

            return false;
        }

        /// <summary>
        /// True once the offered hand has held the same live card count for a short settling
        /// window. Pick N Cards / Pick Phase Improvements build the hand one card at a time,
        /// so acting on a half-built hand offers cards that are about to be replaced.
        /// </summary>
        internal static bool IsOfferedHandReady()
        {
            var alive = GetOfferedCards().Count;
            if (alive == 0)
            {
                _lastSpawnCount = 0;
                return false;
            }

            if (alive != _lastSpawnCount)
            {
                _lastSpawnCount = alive;
                _spawnStableSince = Time.unscaledTime;
                return false;
            }

            return Time.unscaledTime - _spawnStableSince >= 0.25f;
        }

        /// <summary>
        /// The prefab CardInfo an offered card object was built from. The spawned object is a
        /// clone, so its own component is only a last-resort fallback.
        /// </summary>
        internal static CardInfo SourceOf(GameObject go)
        {
            if (go == null) return null;
            var visual = go.GetComponent<CardInfo>();
            if (visual == null) return null;
            if (CardChoice.instance == null) return visual.sourceCard ?? visual;
            return CardChoice.instance.GetSourceCard(visual) ?? visual.sourceCard ?? visual;
        }

        private static Player DesignateFromTeam(Player[] team)
        {
            if (team == null || team.Length == 0) return null;
            Player local = null;
            Player lowest = null;
            foreach (var player in team)
            {
                if (player == null) continue;
                if (lowest == null || player.playerID < lowest.playerID) lowest = player;
                if (local == null && LocalPlayerUtil.IsLocallyControlled(player)) local = player;
            }

            return local ?? lowest;
        }
    }
}
