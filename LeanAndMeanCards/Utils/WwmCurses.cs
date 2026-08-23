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
