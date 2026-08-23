using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Photon.Pun;
using UnboundLib;
using UnboundLib.Networking;
using UnityEngine;

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

            CardTargetUi.OpenSandbag(user, target =>
            {
                if (target == null) return;
                NetworkingManager.RPC(typeof(SandbagManager), nameof(RPCA_RerollTarget), user.playerID, target.playerID);
            });
        }

        [UnboundRPC]
        public static void RPCA_RerollTarget(int userId, int targetId)
        {
            var user = PlayerManager.instance.players.FirstOrDefault(p => p.playerID == userId);
            var target = PlayerManager.instance.players.FirstOrDefault(p => p.playerID == targetId);
            if (user == null || target == null) return;

            if (!(PhotonNetwork.OfflineMode || PhotonNetwork.IsMasterClient)) return;

            if (Plugin.Configs.SandbagOncePerGame.Value && UsedThisGame.Contains(userId))
            {
                NotifySandbagResult(userId, false, "Sandbag already used this game.");
                return;
            }

            if (ItemShopGuard.AnyPlayerInShop())
            {
                Plugin.Instance.LogWarn("Sandbag blocked - shop open.");
                NotifySandbagResult(userId, false, "Can't sandbag during a shop.");
                return;
            }

            var managerType = AccessTools.TypeByName("WillsWackyManagers.Utils.RerollManager");
            var instanceProp = managerType == null ? null : AccessTools.Property(managerType, "instance");
            var manager = instanceProp?.GetValue(null);
            if (manager == null)
            {
                Plugin.Instance.LogWarn("Sandbag failed - RerollManager missing.");
                NotifySandbagResult(userId, false, "Sandbag failed.");
                return;
            }

            NetworkingManager.RPC(typeof(SandbagManager), nameof(RPCA_SyncSandbagUsed), userId);
            Plugin.Instance.StartCoroutine(RerollTargetRoutine(target, managerType, manager));
            NotifySandbagResult(userId, true, $"Sandbagged player {targetId + 1}.");
            Plugin.Instance.Log($"Player {userId} sandbagged player {targetId}'s hand.");
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

        private static IEnumerator RerollTargetRoutine(Player target, Type managerType, object manager)
        {
            var rerollMethod = AccessTools.Method(managerType, "Reroll", new[] { typeof(Player), typeof(bool) });
            if (rerollMethod == null)
            {
                Plugin.Instance.LogWarn("Sandbag failed - Reroll method missing.");
                QueuePendingReroll(manager, managerType, target);
                yield break;
            }

            var routine = rerollMethod.Invoke(manager, new object[] { target, false }) as IEnumerator;
            if (routine == null)
            {
                QueuePendingReroll(manager, managerType, target);
                yield break;
            }

            while (routine.MoveNext())
            {
                yield return routine.Current;
            }
        }

        private static void QueuePendingReroll(object manager, Type managerType, Player target)
        {
            try
            {
                var listField = AccessTools.Field(managerType, "rerollPlayers");
                if (listField?.GetValue(manager) is IList list)
                {
                    if (!list.Contains(target)) list.Add(target);
                }

                var flagField = AccessTools.Field(managerType, "reroll");
                if (flagField != null) flagField.SetValue(manager, true);
            }
            catch (Exception ex)
            {
                Plugin.Instance.LogWarn($"Sandbag queue fallback failed: {ex.Message}");
            }
        }
    }
}
