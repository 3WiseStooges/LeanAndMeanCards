using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using LeanAndMeanCards.Cards;
using LeanAndMeanCards.Utils;
using UnboundLib;
using UnboundLib.GameModes;
using UnityEngine;

namespace LeanAndMeanCards
{
    [BepInDependency("com.willis.rounds.unbound", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("pykess.rounds.plugins.moddingutils", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("pykess.rounds.plugins.cardchoicespawnuniquecardpatch", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("com.rsmind.rounds.fancycardbar", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("root.rarity.lib", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.willuwontu.rounds.managers", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.bukey.rounds.mulliganmadness", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInPlugin(ModId, ModName, Version)]
    [BepInProcess("Rounds.exe")]
    public class Plugin : BaseUnityPlugin
    {
        public const string ModId = "com.ljindustries.rounds.leanandmeancards";
        public const string ModName = "Lean and Mean Cards";
        public const string Version = "1.2.3";
        public const string ModInitials = "LMC";
        public const string CardsMenuName = "LeanAndMeanCards";

        public static Plugin Instance { get; private set; }

        internal static Configs Configs;

        private void Awake()
        {
            Instance = this;
            Configs = new Configs(Config);

            // Patch per type so one unloadable type cannot abort every patch in this assembly.
            // (Unity Mono chokes on IsReadOnlyAttribute, which readonly structs emit — a bare
            // PatchAll then leaves the whole mod unpatched instead of skipping one type.)
            try
            {
                var harmony = new Harmony(ModId);
                Type[] types;
                try
                {
                    types = typeof(Plugin).Assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types;
                    Logger.LogWarning($"GetTypes partial load: {ex.LoaderExceptions?.Length ?? 0} loader error(s)");
                }

                foreach (var type in types)
                {
                    if (type == null) continue;
                    try
                    {
                        harmony.CreateClassProcessor(type).Patch();
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning($"Harmony skip {type.FullName}: {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Harmony patching failed: {ex}");
            }
        }

        private void Start()
        {
            CardRegistration.RegisterAll();
            CardArtFactory.BindLoadedCardInfos();
            BozoShoesRuntime.RegisterHooks();
            DynamiteBlast.RegisterHooks();
            SilverEggManager.RegisterHooks();
            CardStatus.Register();

            Instance.ExecuteAfterSeconds(0.8f, DynamiteBlast.Warmup);
            Instance.ExecuteAfterSeconds(2.5f, DynamiteBlast.Warmup);
            // BuildCard is delayed 2 frames and SetupCard runs before Unbound sets
            // cardArt / cardName, so the immediate bind above often sees no cards yet.
            Instance.ExecuteAfterSeconds(0.5f, CardArtFactory.BindLoadedCardInfos);
            Instance.ExecuteAfterSeconds(2.5f, CardArtFactory.BindLoadedCardInfos);

            gameObject.GetOrAddComponent<DraftSniperTicker>();

            GameModeManager.AddHook(GameModeHooks.HookGameStart, OnGameStart);
            GameModeManager.AddHook(GameModeHooks.HookPlayerPickStart, OnPlayerPickStart);
            GameModeManager.AddHook(GameModeHooks.HookPlayerPickEnd, OnPlayerPickEnd);
            GameModeManager.AddHook(GameModeHooks.HookPickEnd, OnPickEnd);
        }

        private static System.Collections.IEnumerator OnGameStart(IGameModeHandler gm)
        {
            StealLedger.ResetForNewGame();
            SandbagManager.ResetForNewGame();
            CurseOnlyPlayers.InvalidateTargets();
            CurseOnlyPlayers.ResetCache();
            DraftSniperManager.ResetForNewGame();
            BozoShoesRuntime.Clear();
            SilverEggManager.ResetForNewGame();
            SafetyNetEscape.Reset();
            yield break;
        }

        private static System.Collections.IEnumerator OnPlayerPickStart(IGameModeHandler gm)
        {
            DraftSniperManager.ResetForPick();
            CurseOnlyPlayers.ResetCache();
            StealLedger.TryOpenDeferredThiefPrompt();
            CardBarMiniIcons.RestampAll();
            Instance.ExecuteAfterSeconds(0.25f, CardBarMiniIcons.RestampAll);
            Instance.ExecuteAfterSeconds(1f, CardBarMiniIcons.RestampAll);
            yield break;
        }

        private static System.Collections.IEnumerator OnPlayerPickEnd(IGameModeHandler gm)
        {
            DraftSniperManager.ResetForPick();
            yield break;
        }

        private static System.Collections.IEnumerator OnPickEnd(IGameModeHandler gm)
        {
            DraftSniperManager.ResetForPick();
            PickPhase.ClearActingPicker();
            yield break;
        }

        internal void Log(string message) => Logger.LogInfo($"[{ModName}] {message}");

        internal void LogWarn(string message) => Logger.LogWarning($"[{ModName}] {message}");
    }

    internal class Configs
    {
        public ConfigEntry<bool> SandbagOncePerGame { get; }
        public ConfigEntry<bool> SoftenCardGlow { get; }
        public ConfigEntry<string> CurseOnlySteamIds { get; }

        public Configs(ConfigFile config)
        {
            // The host's value decides: the once-per-game gate is enforced inside the
            // master-only branch of SandbagManager. A client's value only affects whether
            // its own "use Sandbag" prompt appears.
            SandbagOncePerGame = config.Bind(
                "Cards", "SandbagOncePerGame", true,
                "Sandbag Simulator can only be used once per game. Set by the host.");

            SoftenCardGlow = config.Bind(
                "Visuals", "SoftenCardGlow", true,
                "Kill the particle glow on this pack's pick cards and mini icons. Sticker outlines stay.");

            // Checked against this machine's own Steam account only — a Steam ID never
            // travels over Photon, so this cannot be applied to anyone else remotely.
            // Requires WillsWackyManagers, and is ignored whenever no curse is drawable.
            CurseOnlySteamIds = config.Bind(
                "Curse Only", "SteamIds", "76561198284769933",
                "Comma-separated Steam64 IDs that are only ever offered curses. Empty disables it.");
        }
    }

    /// <summary>
    /// Keeps PickPhase's acting-picker id current. Postfix only — never alters the pick.
    /// </summary>
    [HarmonyPatch(typeof(CardChoice), nameof(CardChoice.StartPick))]
    internal static class StartPickTrackerPatch
    {
        private static void Postfix(int pickerIDToSet) => PickPhase.NoteActingPicker(pickerIDToSet);
    }

    internal sealed class DraftSniperTicker : MonoBehaviour
    {
        private void Update() => DraftSniperManager.Tick();
    }
}
