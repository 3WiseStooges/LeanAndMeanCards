using System.Reflection;
using HarmonyLib;

namespace LeanAndMeanCards.Utils
{
    internal static class ItemShopGuard
    {
        private static MethodInfo _playerIsInShop;

        internal static bool AnyPlayerInShop()
        {
            if (PlayerManager.instance?.players == null) return false;

            _playerIsInShop ??= ResolvePlayerIsInShop();
            if (_playerIsInShop == null) return false;

            foreach (var player in PlayerManager.instance.players)
            {
                if (player == null) continue;
                try
                {
                    if ((bool)_playerIsInShop.Invoke(null, new object[] { player })) return true;
                }
                catch
                {
                    // ItemShops not loaded or signature changed
                }
            }

            return false;
        }

        private static MethodInfo ResolvePlayerIsInShop()
        {
            var extensions = AccessTools.TypeByName("ItemShops.Extensions.PlayerExtension");
            if (extensions != null)
            {
                var method = AccessTools.Method(extensions, "PlayerIsInShop", new[] { typeof(Player) });
                if (method != null) return method;
            }

            var utils = AccessTools.TypeByName("ItemShops.Utils.ShopManager");
            if (utils != null)
            {
                return AccessTools.Method(utils, "PlayerIsInShop", new[] { typeof(Player) });
            }

            return null;
        }
    }
}
