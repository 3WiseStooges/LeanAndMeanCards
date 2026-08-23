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

        private static int _actingPickerId = -1;
        private static int _lastSpawnCount;
        private static float _spawnStableSince;

        internal static void NoteActingPicker(int pickerId) => _actingPickerId = pickerId;

        internal static void ClearActingPicker()
        {
            _actingPickerId = -1;
            _lastSpawnCount = 0;
            _spawnStableSince = 0f;
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

        internal static List<GameObject> GetReadySpawnedCards()
        {
            var spawned = GetSpawnedCards();
            if (spawned == null) return null;

            var ready = new List<GameObject>();
            foreach (var go in spawned)
            {
                if (go == null) continue;
                if (go.GetComponent<CardInfo>() == null) continue;
                var view = go.GetComponent<PhotonView>();
                if (view != null && view.ViewID == 0) continue;
                ready.Add(go);
            }

            return ready;
        }

        /// <summary>
        /// True once the offered hand has held the same live card count for a short settling
        /// window. Pick N Cards / Pick Phase Improvements build the hand one card at a time,
        /// so acting on a half-built hand offers cards that are about to be replaced.
        /// </summary>
        internal static bool IsOfferedHandReady()
        {
            var spawned = GetSpawnedCards();
            if (spawned == null || spawned.Count == 0)
            {
                _lastSpawnCount = 0;
                return false;
            }

            // Drop destroyed Photon stubs so a wiped hand is not treated as "ready".
            var alive = 0;
            for (var i = 0; i < spawned.Count; i++)
            {
                if (spawned[i] != null) alive++;
            }

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
