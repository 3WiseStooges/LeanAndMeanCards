using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Photon.Pun;
using UnboundLib;
using UnboundLib.Networking;

namespace LeanAndMeanCards.Utils
{
    public static class SandbagManager
    {
        private static readonly HashSet<int> UsedThisGame = new HashSet<int>();

        internal static void ResetForNewGame() => UsedThisGame.Clear();

        internal static bool HasRemaining(Player player) =>
            player != null
            && (!Plugin.Configs.SandbagOncePerGame.Value || !UsedThisGame.Contains(player.playerID));

        internal static void TryPromptSandbag(Player user)
        {
            if (user == null || !HasRemaining(user)) return;
            if (CardTargetUi.IsOpen) return;
            if (ItemShopGuard.AnyPlayerInShop())
            {
                PlayerNotice.Show(user, "Can't sandbag during a shop.");
                return;
            }

            PickUiHold.Push();
            CardTargetUi.OpenSandbag(
                user,
                target =>
                {
                    if (target == null)
                    {
                        PickUiHold.Pop();
                        return;
                    }

                    NetworkingManager.RPC(
                        typeof(SandbagManager),
                        nameof(RPCA_RerollTarget),
                        user.playerID,
                        target.playerID);
                },
                onCancel: PickUiHold.Pop);
        }

        [UnboundRPC]
        public static void RPCA_RerollTarget(int userId, int targetId)
        {
            var user = PlayerManager.instance.players.FirstOrDefault(p => p.playerID == userId);
            var target = PlayerManager.instance.players.FirstOrDefault(p => p.playerID == targetId);
            if (user == null || target == null)
            {
                PickUiHold.Pop();
                return;
            }

            if (!(PhotonNetwork.OfflineMode || PhotonNetwork.IsMasterClient)) return;

            if (Plugin.Configs.SandbagOncePerGame.Value && UsedThisGame.Contains(userId))
            {
                PickUiHold.Pop();
                NotifySandbagResult(userId, false, "Sandbag already used this game.");
                return;
            }

            if (ItemShopGuard.AnyPlayerInShop())
            {
                PickUiHold.Pop();
                Plugin.Instance.LogWarn("Sandbag blocked - shop open.");
                NotifySandbagResult(userId, false, "Can't sandbag during a shop.");
                return;
            }

            var managerType = AccessTools.TypeByName("WillsWackyManagers.Utils.RerollManager");
            var instanceProp = managerType == null ? null : AccessTools.Property(managerType, "instance");
            var manager = instanceProp?.GetValue(null);
            if (manager == null)
            {
                PickUiHold.Pop();
                Plugin.Instance.LogWarn("Sandbag failed - RerollManager missing.");
                NotifySandbagResult(userId, false, "Sandbag failed (Wills Wacky Managers missing).");
                return;
            }

            if (!QueuePendingReroll(manager, managerType, target))
            {
                PickUiHold.Pop();
                NotifySandbagResult(userId, false, "Sandbag failed.");
                return;
            }

            NetworkingManager.RPC(typeof(SandbagManager), nameof(RPCA_SyncSandbagUsed), userId);
            PickUiHold.Pop();
            NotifySandbagResult(userId, true, $"Sandbagged {PlayerLabels.For(target)}.");
            Plugin.Instance.Log($"Player {userId} sandbagged {PlayerLabels.For(target)} (id {targetId}).");
        }

        [UnboundRPC]
        public static void RPCA_SyncSandbagUsed(int userId)
        {
            if (Plugin.Configs.SandbagOncePerGame.Value)
            {
                UsedThisGame.Add(userId);
            }
        }

        [UnboundRPC]
        public static void RPCA_SandbagResult(int userId, bool ok, string message)
        {
            var user = PlayerManager.instance.players.FirstOrDefault(p => p.playerID == userId);
            if (user == null || string.IsNullOrEmpty(message)) return;
            PlayerNotice.Show(user, message);
        }

        private static void NotifySandbagResult(int userId, bool ok, string message)
        {
            NetworkingManager.RPC(typeof(SandbagManager), nameof(RPCA_SandbagResult), userId, ok, message ?? "");
        }

        private static bool QueuePendingReroll(object manager, Type managerType, Player target)
        {
            try
            {
                var list = GetMemberValue(manager, managerType, "rerollPlayers") as IList;
                if (list == null)
                {
                    Plugin.Instance.LogWarn("Sandbag failed - rerollPlayers missing.");
                    return false;
                }

                if (!list.Contains(target)) list.Add(target);

                if (!SetMemberValue(manager, managerType, "reroll", true))
                {
                    Plugin.Instance.LogWarn("Sandbag failed - reroll flag missing.");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Plugin.Instance.LogWarn($"Sandbag queue failed: {ex.Message}");
                return false;
            }
        }

        private static object GetMemberValue(object instance, Type type, string name)
        {
            var prop = AccessTools.Property(type, name);
            if (prop != null) return prop.GetValue(instance, null);
            var field = AccessTools.Field(type, name);
            return field?.GetValue(instance);
        }

        private static bool SetMemberValue(object instance, Type type, string name, object value)
        {
            var prop = AccessTools.Property(type, name);
            if (prop != null && prop.CanWrite)
            {
                prop.SetValue(instance, value, null);
                return true;
            }

            var field = AccessTools.Field(type, name);
            if (field == null) return false;
            field.SetValue(instance, value);
            return true;
        }
    }
}
