using System;
using System.Reflection;
using HarmonyLib;
using LeanAndMeanCards.Cards;

namespace LeanAndMeanCards.Utils
{
    /// <summary>
    /// Publishes this pack's per-player statuses to willuwontu's TabInfo when it is installed.
    ///
    /// Reflection-only and entirely optional — this pack must never reference or ship
    /// TabInfo.dll. Shipping a stub that claims another mod's plugin GUID collides with the
    /// real mod and forces users to disable it.
    /// </summary>
    internal static class CardStatus
    {
        private const string CategoryName = "Lean and Mean";
        private const int CategoryPriority = 45;

        private static MethodInfo _registerStat;
        private static object _category;

        internal static void Register()
        {
            if (!Bind()) return;

            try
            {
                Stat("Bozo Shoes",
                    player => BozoShoesRuntime.IsMarked(player),
                    _ => "Clown shoes, +50% knockback");

                Stat("Safety Net",
                    player => CardOwnership.Has(player, SafetyNet.Card),
                    _ => "No edge damage, OOB escape kill");

                Stat("TASER TASER TASER",
                    player => CardOwnership.Has(player, TaserTaserTaser.Card),
                    _ => "+0.5s stun on hit");

                Stat("Yeet Cannon",
                    player => CardOwnership.Has(player, YeetCannon.Card),
                    _ => "Strong kick away from gun");

                Stat("Dynamite",
                    player => CardOwnership.Has(player, Dynamite.Card),
                    _ => "Delayed blast, huge knockback");

                Stat("Draft Sniper",
                    player => DraftSniperManager.Remaining(player) > 0,
                    player => DraftSniperManager.Remaining(player) == 1
                        ? "Click to lock"
                        : DraftSniperManager.Remaining(player) + " locks");

                Stat("Silver Egg",
                    SilverEggManager.ShowStat,
                    SilverEggManager.StatusText);

                Plugin.Instance?.Log("Registered card statuses with TabInfo.");
            }
            catch (Exception ex)
            {
                Plugin.Instance?.LogWarn($"TabInfo stat registration skipped: {ex.Message}");
            }
        }

        private static bool Bind()
        {
            var manager = AccessTools.TypeByName("TabInfo.Utils.TabInfoManager");
            if (manager == null) return false;

            try
            {
                var registerCategory = AccessTools.Method(manager, "RegisterCategory");
                _registerStat = AccessTools.Method(manager, "RegisterStat");
                if (registerCategory == null || _registerStat == null) return false;

                _category = registerCategory.Invoke(null, new object[] { CategoryName, CategoryPriority });
                return _category != null;
            }
            catch (Exception ex)
            {
                Plugin.Instance?.LogWarn($"TabInfo bind failed: {ex.Message}");
                return false;
            }
        }

        private static void Stat(string name, Func<Player, bool> visible, Func<Player, string> value)
        {
            var parameters = _registerStat.GetParameters();
            if (parameters.Length != 4) return;

            // TabInfo takes its own delegate types; build them from ours.
            var visibleDelegate = Delegate.CreateDelegate(parameters[2].ParameterType, visible.Target, visible.Method);
            var valueDelegate = Delegate.CreateDelegate(parameters[3].ParameterType, value.Target, value.Method);
            _registerStat.Invoke(null, new[] { _category, name, visibleDelegate, valueDelegate });
        }
    }
}
