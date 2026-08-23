using System;
using System.Reflection;
using HarmonyLib;

namespace LeanAndMeanCards.Utils
{
    /// <summary>
    /// Optional, reflection-only awareness of MulliganMadness' Take All.
    ///
    /// Draft Sniper and Sandbag Simulator must not act while another mod is bulk-collecting
    /// the offered hand, or they mutate a hand that is mid-animation. This reads that flag
    /// without a hard reference, so the pack works standalone.
    /// </summary>
    internal static class ExternalPickState
    {
        private const string TakeAllTypeName = "MulliganMadness.Utils.TakeAllManager";

        private static bool _probed;
        private static FieldInfo _collectingAll;

        /// <summary>
        /// True while MulliganMadness is animating a Take All. False whenever it is absent.
        /// </summary>
        internal static bool IsBulkCollecting
        {
            get
            {
                Probe();
                if (_collectingAll == null) return false;
                try
                {
                    return _collectingAll.GetValue(null) is bool flag && flag;
                }
                catch
                {
                    return false;
                }
            }
        }

        private static void Probe()
        {
            if (_probed) return;
            _probed = true;

            try
            {
                var type = AccessTools.TypeByName(TakeAllTypeName);
                if (type == null) return;

                var field = AccessTools.Field(type, "CollectingAll");
                if (field == null || field.FieldType != typeof(bool) || !field.IsStatic) return;

                _collectingAll = field;
                Plugin.Instance?.Log("MulliganMadness detected — Take All collection will be respected.");
            }
            catch (Exception ex)
            {
                Plugin.Instance?.LogWarn($"ExternalPickState probe failed: {ex.Message}");
            }
        }
    }
}
