using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace LeanAndMeanCards.Utils
{
    /// <summary>
    /// Restricts listed Steam accounts to curse-only card offers.
    ///
    /// Runs entirely on the listed player's own machine, because a Steam ID never
    /// crosses the wire: ROUNDS sets PhotonNetwork.LocalPlayer.NickName to the Steam
    /// *persona name* and nothing else, so no client can learn another client's Steam
    /// ID. That is fine here — vanilla CardChoice.Pick starts ReplaceCards only when
    /// the picker's own view IsMine, so the picker's machine is exactly the one that
    /// builds their hand.
    ///
    /// Consequence worth knowing: the person affected can turn it off in their own
    /// config, and it does nothing if they do not have this mod installed.
    /// </summary>
    internal static class CurseOnlyPlayers
    {
        private static ulong[] _targets = Array.Empty<ulong>();
        private static bool _parsed;

        private static bool _steamProbed;
        private static MethodInfo _getSteamId;
        private static FieldInfo _steamIdValue;
        private static ulong _localSteamId;

        // PlayerIsAllowedCurse calls back into PlayerIsAllowedCard, which this class
        // postfixes. Without a guard that recurses until the stack runs out.
        [ThreadStatic] private static bool _reentrant;

        // CountDrawableCurses walks every active curse and asks ModdingUtils about each
        // one. SpawnUniqueCard can ask about dozens of cards per offer, so cache the
        // answer for the current pick rather than recomputing it every time.
        private static int _cachedCount = -1;
        private static int _cachedForPlayer = -1;
        private static float _cachedAt;

        private static bool _announced;
        private static int _cachedLocalId = -1;
        private static int _localIdCachedAt = -1;

        internal static void ResetCache()
        {
            _cachedCount = -1;
            _cachedForPlayer = -1;
            _cachedLocalId = -1;
            _localIdCachedAt = -1;
        }

        internal static void InvalidateTargets()
        {
            _parsed = false;
        }

        /// <summary>
        /// True when this machine's Steam account is listed and <paramref name="player"/>
        /// is the local player.
        /// </summary>
        internal static bool AppliesTo(Player player)
        {
            if (player == null) return false;

            ParseTargets();
            if (_targets.Length == 0) return false;

            var id = LocalSteamId();
            if (id == 0UL) return false;

            var listed = false;
            foreach (var t in _targets)
            {
                if (t == id) { listed = true; break; }
            }

            if (!listed)
            {
                if (!_announced)
                {
                    _announced = true;
                    Plugin.Instance?.Log(
                        $"Curse-only is armed for {_targets.Length} account(s); this machine " +
                        $"({id}) is not one of them, so offers are unchanged.");
                }

                return false;
            }

            if (!_announced)
            {
                _announced = true;
                Plugin.Instance?.Log($"Curse-only is ACTIVE for this machine ({id}).");
            }

            // Compare by playerID rather than by reference: Unity's overloaded == treats a
            // destroyed object as null, and a mid-round respawn can hand out a fresh Player.
            // Cached per pick — PlayerIsAllowedCard runs once per card per offer slot, and
            // LocalPlayer() scans every player calling GetComponent each time.
            if (_localIdCachedAt != Time.frameCount)
            {
                var local = LocalPlayerUtil.LocalPlayer();
                _cachedLocalId = local != null ? local.playerID : -1;
                _localIdCachedAt = Time.frameCount;
            }

            return _cachedLocalId >= 0 && _cachedLocalId == player.playerID;
        }

        /// <summary>
        /// Whether the curse-only filter can safely be applied to this player's offer.
        ///
        /// If no curse is currently drawable, rejecting everything else empties the pool.
        /// CardChoiceSpawnUniqueCardPatch (a hard dependency) then hands out Null cards
        /// rather than recursing, so this is not a crash — but a hand of Nulls is a worse
        /// joke than a hand of curses. Fall back to normal cards instead.
        ///
        /// Curses are usually excluded from ordinary draws, so this is the common case
        /// whenever WillsWackyManagers is missing or every curse is toggled off.
        /// </summary>
        internal static bool CanRestrict(Player player)
        {
            if (player == null || !WwmCurses.IsAvailable) return false;

            if (_cachedForPlayer == player.playerID && _cachedCount >= 0 &&
                Time.unscaledTime - _cachedAt < 1.0f)
            {
                return _cachedCount > 0;
            }

            var count = WwmCurses.CountDrawableCurses(player);
            _cachedCount = count;
            _cachedForPlayer = player.playerID;
            _cachedAt = Time.unscaledTime;

            if (count == 0)
            {
                Plugin.Instance?.LogWarn(
                    "Curse-only is configured for this account but no curse is currently " +
                    "drawable; offering normal cards instead so the pick cannot soft-lock.");
            }
            else
            {
                Plugin.Instance?.Log($"Curse-only: restricting this offer to {count} drawable curse(s).");
            }

            return count > 0;
        }

        private static void ParseTargets()
        {
            if (_parsed) return;
            _parsed = true;

            var raw = Plugin.Configs?.CurseOnlySteamIds?.Value ?? "";
            var ids = new List<ulong>();
            foreach (var part in raw.Split(',', ';', ' '))
            {
                var s = part.Trim();
                if (s.Length == 0) continue;
                if (ulong.TryParse(s, out var id) && id > 0UL) ids.Add(id);
                else Plugin.Instance?.LogWarn($"Ignoring malformed Steam ID '{s}' in CurseOnlySteamIds.");
            }

            _targets = ids.ToArray();
        }

        /// <summary>
        /// This machine's Steam ID, or 0 when Steamworks is unavailable.
        /// Reflection-only so a non-Steam build simply disables the feature.
        /// </summary>
        private static ulong LocalSteamId()
        {
            if (_localSteamId != 0UL) return _localSteamId;
            if (_steamProbed && _getSteamId == null) return 0UL;

            if (!_steamProbed)
            {
                _steamProbed = true;
                try
                {
                    var steamUser = AccessTools.TypeByName("Steamworks.SteamUser");
                    _getSteamId = steamUser == null ? null : AccessTools.Method(steamUser, "GetSteamID");
                    var cSteamId = AccessTools.TypeByName("Steamworks.CSteamID");
                    _steamIdValue = cSteamId == null ? null : AccessTools.Field(cSteamId, "m_SteamID");
                    if (_getSteamId == null || _steamIdValue == null)
                    {
                        _getSteamId = null;
                        Plugin.Instance?.Log("Steamworks not available; curse-only targeting is disabled.");
                    }
                }
                catch (Exception ex)
                {
                    _getSteamId = null;
                    Plugin.Instance?.LogWarn($"Steamworks probe failed: {ex.Message}");
                }
            }

            if (_getSteamId == null) return 0UL;

            try
            {
                var boxed = _getSteamId.Invoke(null, null);
                if (boxed == null) return 0UL;
                if (_steamIdValue.GetValue(boxed) is ulong id) _localSteamId = id;
            }
            catch (Exception ex)
            {
                // Steam not running / API not initialised yet. Retry on the next call.
                Plugin.Instance?.LogWarn($"Could not read local Steam ID: {ex.Message}");
            }

            return _localSteamId;
        }

        internal static bool Reentrant
        {
            get => _reentrant;
            set => _reentrant = value;
        }
    }
}
