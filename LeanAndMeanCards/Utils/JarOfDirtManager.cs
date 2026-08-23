using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CardChoiceSpawnUniqueCardPatch.CustomCategories;
using HarmonyLib;
using ModdingUtils.Utils;
using CardsApi = ModdingUtils.Utils.Cards;
using Photon.Pun;
using UnboundLib;
using UnboundLib.Networking;
using UnityEngine;

namespace LeanAndMeanCards.Utils
{
    public static class JarOfDirtManager
    {
        private static Type _nullCardInfo;
        private static MethodInfo _getTreasure;

        internal static void TryConvert(Player player)
        {
            if (player == null) return;
            NetworkingManager.RPC(typeof(JarOfDirtManager), nameof(RPCA_ConvertNulls), player.playerID);
        }

        [UnboundRPC]
        public static void RPCA_ConvertNulls(int playerId)
        {
            var player = PlayerManager.instance?.players?.FirstOrDefault(p => p.playerID == playerId);
            if (player == null)
            {
                Plugin.Instance.LogWarn("Jar of Dirt RPC failed - player missing.");
                return;
            }

            if (!(PhotonNetwork.OfflineMode || PhotonNetwork.IsMasterClient)) return;

            var cards = player.data?.currentCards;
            if (cards == null)
            {
                Plugin.Instance.LogWarn("Jar of Dirt failed - no card list.");
                return;
            }

            var treasures = CollectEnabledTreasures();
            if (treasures.Count == 0)
            {
                Plugin.Instance.LogWarn("Jar of Dirt found no enabled treasures (KeysCards missing or all disabled).");
                return;
            }

            var indices = new List<int>();
            for (var i = 0; i < cards.Count; i++)
            {
                if (IsEligibleNull(cards[i])) indices.Add(i);
            }

            if (indices.Count == 0)
            {
                Plugin.Instance.Log($"Jar of Dirt: player {playerId} had no eligible Nulls.");
                return;
            }

            var replacements = new List<CardInfo>(indices.Count);
            for (var n = 0; n < indices.Count; n++)
            {
                replacements.Add(PickTreasure(treasures));
            }

            for (var n = indices.Count - 1; n >= 0; n--)
            {
                CardsApi.instance.RemoveCardFromPlayer(player, indices[n]);
            }

            foreach (var treasure in replacements)
            {
                CardsApi.instance.AddCardToPlayer(player, treasure, false, "", 2f, 2f, true);
            }

            Plugin.Instance.Log($"Jar of Dirt replaced {replacements.Count} Null(s) with treasure for player {playerId}.");
        }

        internal static bool HasEligibleNulls(Player player)
        {
            var cards = player?.data?.currentCards;
            if (cards == null) return false;
            foreach (var card in cards)
            {
                if (IsEligibleNull(card)) return true;
            }

            return false;
        }

        private static bool IsEligibleNull(CardInfo card)
        {
            if (card == null) return false;
            if (!IsNullPlaceholder(card)) return false;

            var source = GetNulledSource(card);
            if (source == null) return true;
            return CardPool.IsActive(source);
        }

        private static bool IsNullPlaceholder(CardInfo card)
        {
            _nullCardInfo ??= AccessTools.TypeByName("Nullmanager.NullCardInfo");
            if (_nullCardInfo != null)
            {
                if (_nullCardInfo.IsInstanceOfType(card)) return true;
                if (card.gameObject != null)
                {
                    var component = card.gameObject.GetComponent(_nullCardInfo)
                                    ?? card.gameObject.GetComponentInChildren(_nullCardInfo);
                    if (component != null) return true;
                }
            }

            var name = (card.cardName ?? "").Trim();
            if (name.StartsWith("[]", StringComparison.Ordinal)) return true;
            if (name.Equals("null", StringComparison.OrdinalIgnoreCase)) return true;
            if (name.Equals("NullCard", StringComparison.OrdinalIgnoreCase)) return true;
            if (name.Equals("Null Card", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static CardInfo GetNulledSource(CardInfo card)
        {
            _nullCardInfo ??= AccessTools.TypeByName("Nullmanager.NullCardInfo");
            if (_nullCardInfo == null || card == null) return null;

            object info = _nullCardInfo.IsInstanceOfType(card) ? card : null;
            if (info == null && card.gameObject != null)
            {
                info = card.gameObject.GetComponent(_nullCardInfo)
                       ?? card.gameObject.GetComponentInChildren(_nullCardInfo);
            }

            if (info == null) return null;

            try
            {
                var field = AccessTools.Field(info.GetType(), "NulledSorce");
                return field?.GetValue(info) as CardInfo;
            }
            catch
            {
                return null;
            }
        }

        private static List<CardInfo> CollectEnabledTreasures()
        {
            var result = new List<CardInfo>();
            var all = CardsApi.all;
            if (all == null) return result;

            CardCategory treasureCat = null;
            try
            {
                treasureCat = CustomCardCategories.instance?.CardCategory("Treasure");
            }
            catch
            {
                treasureCat = null;
            }

            foreach (var card in all)
            {
                if (card == null || !HasTreasureCategory(card, treasureCat)) continue;
                if (!CardPool.IsActive(card)) continue;
                result.Add(card);
            }

            return result;
        }

        private static bool HasTreasureCategory(CardInfo card, CardCategory treasureCat)
        {
            if (card.categories == null) return false;
            foreach (var category in card.categories)
            {
                if (category == null) continue;
                if (treasureCat != null && category == treasureCat) return true;
                var name = category.name ?? "";
                if (name.Equals("Treasure", StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }

        private static CardInfo PickTreasure(List<CardInfo> pool)
        {
            var fromKeys = TryGetTreasureFromKeys();
            if (fromKeys != null && CardPool.IsActive(fromKeys)) return fromKeys;
            return pool[UnityEngine.Random.Range(0, pool.Count)];
        }

        private static CardInfo TryGetTreasureFromKeys()
        {
            try
            {
                if (_getTreasure == null)
                {
                    var type = AccessTools.TypeByName("KeysCards.CardCheck")
                               ?? AccessTools.TypeByName("KeysCards.Cards.CardCheck");
                    _getTreasure = type == null ? null : AccessTools.Method(type, "getTreasure", Type.EmptyTypes);
                }

                if (_getTreasure == null) return null;
                if (!(_getTreasure.Invoke(null, null) is string name) || string.IsNullOrEmpty(name)) return null;
                name = CardEncoding.StripClone(name);
                try { return CardsApi.instance.GetCardWithObjectName(name); }
                catch { return null; }
            }
            catch
            {
                return null;
            }
        }
    }
}
