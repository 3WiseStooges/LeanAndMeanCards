using System;
using BepInEx.Configuration;

namespace LeanAndMeanCards.Utils
{
    /// <summary>
    /// Clears settings this mod used to write but no longer binds.
    ///
    /// Dropping a Bind from the code is not enough to get a setting out of anyone's
    /// config: BepInEx keeps a key it cannot match to a Bind as an orphaned entry and
    /// writes it back out on every Save, so the old value survives updates forever.
    /// Binding it deliberately adopts the orphan — which is what takes it out of that
    /// orphan list — and removing the entry then leaves nothing for Save to write.
    /// </summary>
    internal static class ConfigMigration
    {
        // The curse-only roster used to be a config default, which put a Steam ID in
        // plain sight in every player's config file. It is compiled in now, as digests
        // (see CurseOnlyRoster), so this clears what 1.1.0–1.2.7 left behind.
        private static readonly ConfigDefinition[] Dropped =
        {
            new ConfigDefinition("Curse Only", "SteamIds"),
        };

        internal static void PurgeDroppedEntries(ConfigFile config)
        {
            if (config == null) return;

            var saveOnSet = config.SaveOnConfigSet;

            try
            {
                // Adopting an orphan counts as setting it, and an intermediate Save would
                // write the stale value straight back into the file we are here to clean.
                config.SaveOnConfigSet = false;

                foreach (var definition in Dropped)
                {
                    config.Bind(definition, "", new ConfigDescription("No longer used."));
                    config.Remove(definition);
                }
            }
            catch (Exception ex)
            {
                Plugin.Instance?.LogWarn($"Could not clear dropped config entries: {ex.Message}");
            }
            finally
            {
                config.SaveOnConfigSet = saveOnSet;
            }

            try
            {
                config.Save();
            }
            catch (Exception ex)
            {
                Plugin.Instance?.LogWarn($"Could not rewrite the config file: {ex.Message}");
            }
        }
    }
}
