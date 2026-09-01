using System.Collections;
using LeanAndMeanCards.Patches;
using UnboundLib.GameModes;

namespace LeanAndMeanCards.Utils
{
    /// <summary>
    /// Round-boundary plumbing for <see cref="Diag"/>: refresh the cached toggle on the way
    /// in, flush the counters on the way out.
    /// </summary>
    internal static class DiagHooks
    {
        internal static void RegisterHooks()
        {
            GameModeManager.AddHook(GameModeHooks.HookRoundStart, OnRoundStart);
            GameModeManager.AddHook(GameModeHooks.HookRoundEnd, OnRoundEnd);
            GameModeManager.AddHook(GameModeHooks.HookGameEnd, OnGameEnd);
        }

        private static IEnumerator OnRoundStart(IGameModeHandler gm)
        {
            Diag.Refresh();
            ImpulseFilter.Refresh();
            yield break;
        }

        private static IEnumerator OnRoundEnd(IGameModeHandler gm)
        {
            Diag.Flush("round-end");
            yield break;
        }

        private static IEnumerator OnGameEnd(IGameModeHandler gm)
        {
            Diag.Flush("game-end");
            Diag.FlushTotals("game-end");
            yield break;
        }
    }
}
