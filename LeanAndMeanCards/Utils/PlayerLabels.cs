using Photon.Pun;
using UnityEngine;

namespace LeanAndMeanCards.Utils
{
    internal static class PlayerLabels
    {
        private static readonly string[] Colors = { "Orange", "Blue", "Red", "Green" };

        internal static string For(Player player)
        {
            if (player == null) return "Unknown";

            var local = LocalPlayerUtil.LocalPlayer();
            if (local != null && local.playerID == player.playerID)
            {
                var mine = SafePhotonNick();
                if (!string.IsNullOrEmpty(mine)) return mine;
                var own = SafeOwnerNick(player);
                return string.IsNullOrEmpty(own) ? "You" : own;
            }

            var nick = SafeOwnerNick(player);
            var localNick = SafePhotonNick();
            // Local bots often inherit the host Photon nick or have none at all.
            if (!string.IsNullOrEmpty(nick) &&
                (string.IsNullOrEmpty(localNick) ||
                 !string.Equals(nick, localNick, System.StringComparison.OrdinalIgnoreCase)))
            {
                return nick;
            }

            return ColorName(player);
        }

        private static string ColorName(Player player)
        {
            var id = player != null ? player.playerID : 0;
            if (id >= 0 && id < Colors.Length) return Colors[id];
            return "Player " + (id + 1);
        }

        private static string SafeOwnerNick(Player player)
        {
            try
            {
                return player?.data?.view?.Owner?.NickName;
            }
            catch
            {
                return null;
            }
        }

        private static string SafePhotonNick()
        {
            try
            {
                return PhotonNetwork.NickName;
            }
            catch
            {
                return null;
            }
        }
    }
}
