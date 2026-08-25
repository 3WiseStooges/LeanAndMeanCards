using System.Collections;
using HarmonyLib;
using LeanAndMeanCards.Utils;

namespace LeanAndMeanCards.Patches
{
    [HarmonyPatch(typeof(CardChoice), "IDoEndPick")]
    internal static class HoldEndPickPatch
    {
        private static void Postfix(ref IEnumerator __result)
        {
            __result = WaitThen(__result);
        }

        private static IEnumerator WaitThen(IEnumerator original)
        {
            while (PickUiHold.ShouldWait)
                yield return null;

            if (original == null) yield break;
            while (original.MoveNext())
                yield return original.Current;
        }
    }
}
