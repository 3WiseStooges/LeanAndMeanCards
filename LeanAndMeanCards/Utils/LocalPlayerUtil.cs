using System;
using Photon.Pun;

namespace LeanAndMeanCards.Utils
{
    /// <summary>
    /// Resolving "which Player object is me" is surprisingly load-bearing: card effects that
    /// guess wrong either fire on the wrong client or not at all. PlayerAPI (local splitscreen),
    /// PhotonView.IsMine, Owner.IsLocal and ActorNumber all disagree in different situations,
    /// so try each in turn and never throw out.
    /// </summary>
    internal static class LocalPlayerUtil
    {
        internal static Player LocalPlayer()
        {
            var players = PlayerManager.instance?.players;
            if (players == null || players.Count == 0) return null;

            try
            {
                if (PhotonNetwork.LocalPlayer != null)
                {
                    var actor = PhotonNetwork.LocalPlayer.ActorNumber;
                    foreach (var player in players)
                    {
                        var view = player?.data?.view ?? player?.GetComponent<PhotonView>();
                        if (view != null && view.OwnerActorNr == actor) return player;
                    }
                }
            }
            catch
            {
                // Photon not ready
            }

            foreach (var player in players)
            {
                if (IsLocallyControlled(player)) return player;
            }

            try
            {
                var nick = PhotonNetwork.NickName;
                if (!string.IsNullOrEmpty(nick))
                {
                    foreach (var player in players)
                    {
                        var name = player?.data?.view?.Owner?.NickName;
                        if (!string.IsNullOrEmpty(name) &&
                            string.Equals(name, nick, StringComparison.OrdinalIgnoreCase))
                        {
                            return player;
                        }
                    }
                }
            }
            catch
            {
                // Photon not ready
            }

            if (PhotonNetwork.OfflineMode) return players[0];
            return null;
        }

        internal static bool IsLocallyControlled(Player player)
        {
            if (player == null) return false;

            try
            {
                var api = player.GetComponent<PlayerAPI>();
                if (api != null && api.enabled) return true;
            }
            catch
            {
                // PlayerAPI missing
            }

            var view = player.data?.view ?? player.GetComponent<PhotonView>();
            if (view == null) return false;

            try
            {
                if (view.IsMine) return true;
            }
            catch
            {
                // view not ready
            }

            try
            {
                if (view.Owner != null && view.Owner.IsLocal) return true;
            }
            catch
            {
                // owner not ready
            }

            try
            {
                var local = PhotonNetwork.LocalPlayer;
                if (local != null && view.OwnerActorNr == local.ActorNumber) return true;
            }
            catch
            {
                // Photon not ready
            }

            return false;
        }
    }
}
