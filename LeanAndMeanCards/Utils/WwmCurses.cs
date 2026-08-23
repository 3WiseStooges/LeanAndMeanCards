using System;
using System.Reflection;
using HarmonyLib;

namespace LeanAndMeanCards.Utils
{
    /// <summary>
    /// Optional reflection bridge to WillsWackyManagers' CurseManager, used so Thief cannot
    /// steal a curse. Reflection rather than an assembly reference keeps WWM a true soft
    /// dependency — without it, nothing is treated as a curse and the rest of the rules apply.
    /// </summary>
    internal static class WwmCurses
    {
        private static bool _probed;
        private static object _instance;
        private static MethodInfo _isCurse;
        private static MethodInfo _getRaw;
        private static MethodInfo _playerIsAllowedCurse;

        internal static bool IsAvailable
        {
            get
            {
                Probe();
                return _isCurse != null;
            }
        }

        /// <summary>
        /// Curses this player could actually be dealt right now.
        ///
        /// GetRaw(activeOnly: true) is the pool after Toggle Cards, and
        /// PlayerIsAllowedCurse applies the same per-player rules a real draw would —
        /// it temporarily lifts WWM's canDrawCurses gate, asks ModdingUtils, then puts
        /// the gate back. Callers use this to avoid ever emptying the draw pool.
        /// </summary>
        internal static int CountDrawableCurses(Player player)
        {
            Probe();
            if (_getRaw == null || _playerIsAllowedCurse == null || player == null) return 0;

            try
            {
                if (!(_getRaw.Invoke(_instance, new object[] { true }) is CardInfo[] curses)) return 0;
                var n = 0;
                foreach (var curse in curses)
                {
                    if (curse == null) continue;
                    if (IsDrawableCurse(player, curse)) n++;
                }

                return n;
            }
            catch
            {
                return 0;
            }
        }

        internal static bool IsDrawableCurse(Player player, CardInfo card)
        {
            Probe();
            if (_playerIsAllowedCurse == null || player == null || card == null) return false;

            try
            {
                return _playerIsAllowedCurse.Invoke(_instance, new object[] { player, card }) is bool ok && ok;
            }
            catch
            {
                return false;
            }
        }

        internal static bool IsCurse(CardInfo card)
        {
            if (card == null) return false;
            Probe();
            if (_isCurse == null) return false;

            try
            {
                return _isCurse.Invoke(_instance, new object[] { card }) is bool result && result;
            }
            catch
            {
                return false;
            }
        }

        private static void Probe()
        {
            // CurseManager.instance is created during startup, so keep retrying until it exists.
            if (_probed && _instance != null) return;

            try
            {
                var type = AccessTools.TypeByName("WillsWackyManagers.Utils.CurseManager");
                if (type == null)
                {
                    _probed = true;
                    return;
                }

                _instance = AccessTools.Property(type, "instance")?.GetValue(null)
                            ?? AccessTools.Field(type, "instance")?.GetValue(null);
                if (_instance == null) return;

                _isCurse = AccessTools.Method(type, "IsCurse", new[] { typeof(CardInfo) });
                _getRaw = AccessTools.Method(type, "GetRaw", new[] { typeof(bool) });
                _playerIsAllowedCurse = AccessTools.Method(
                    type, "PlayerIsAllowedCurse", new[] { typeof(Player), typeof(CardInfo) });
                _probed = true;
            }
            catch (Exception ex)
            {
                _probed = true;
                Plugin.Instance?.LogWarn($"WillsWackyManagers probe failed: {ex.Message}");
            }
        }
    }
}
