using UnboundLib;
using UnboundLib.Networking;

namespace LeanAndMeanCards.Utils
{
    /// <summary>
    /// Sandbag / Thief UI only opens on the picker. Without a synced hold, everyone else
    /// finishes the pick and the match starts under the overlay.
    /// </summary>
    internal static class PickUiHold
    {
        private static int _holds;

        internal static bool ShouldWait => _holds > 0;

        internal static void Reset() => _holds = 0;

        internal static void Push()
        {
            NetworkingManager.RPC(typeof(PickUiHold), nameof(RPCA_Delta), 1);
        }

        internal static void Pop()
        {
            NetworkingManager.RPC(typeof(PickUiHold), nameof(RPCA_Delta), -1);
        }

        [UnboundRPC]
        public static void RPCA_Delta(int delta)
        {
            _holds = System.Math.Max(0, _holds + delta);
        }
    }
}
