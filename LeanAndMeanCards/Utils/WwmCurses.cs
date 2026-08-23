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
        private static bool _typeMissing;
        private static Type _type;
        private static PropertyInfo _instanceProperty;
        private static FieldInfo _instanceField;
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
            if (_probed) return;

            try
            {
                // Resolve the type exactly once and cache it even when CurseManager.instance
                // is not up yet. AccessTools.TypeByName walks every loaded assembly calling
                // GetTypes(), which is expensive and noisy — the earlier version re-ran it on
                // every call while instance was null, and IsCurse is called once per card per
                // offer slot, so a whole card pool meant thousands of full assembly scans.
                if (_type == null && !_typeMissing)
                {
                    _type = AccessTools.TypeByName("WillsWackyManagers.Utils.CurseManager");
                    if (_type == null)
                    {
                        _typeMissing = true;
                        _probed = true;
                        return;
                    }

                    _instanceProperty = AccessTools.Property(_type, "instance");
                    _instanceField = _instanceProperty == null ? AccessTools.Field(_type, "instance") : null;
                }

                if (_type == null) return;

                // Only the instance read is retried, and that is a cheap property get.
                _instance = _instanceProperty != null
                    ? _instanceProperty.GetValue(null)
                    : _instanceField?.GetValue(null);
                if (_instance == null) return;

                _isCurse = AccessTools.Method(_type, "IsCurse", new[] { typeof(CardInfo) });
                _getRaw = AccessTools.Method(_type, "GetRaw", new[] { typeof(bool) });
                _playerIsAllowedCurse = AccessTools.Method(
                    _type, "PlayerIsAllowedCurse", new[] { typeof(Player), typeof(CardInfo) });
                _probed = true;
                Plugin.Instance?.Log("WillsWackyManagers curse API bound.");
            }
            catch (Exception ex)
            {
                _probed = true;
                Plugin.Instance?.LogWarn($"WillsWackyManagers probe failed: {ex.Message}");
            }
        }
    }
}
