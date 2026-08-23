using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LeanAndMeanCards.Utils
{
    internal static class CardBarMiniIcons
    {
        private const string ChildName = "MM_MiniIcon";
        private static readonly Dictionary<string, GameObject> FancyPrefabs = new Dictionary<string, GameObject>();
        private static GameObject _fancyHolder;
        private static Type _fancyIconType;
        private static bool _fancyResolved;
        private static readonly FieldInfo CardField = AccessTools.Field(typeof(CardBarButton), "card");

        internal static bool IsMmCard(CardInfo card)
        {
            if (card?.cardArt == null) return false;
            return card.cardArt.GetComponent<MmCardArtTag>() != null;
        }

        internal static void ApplyToLatestButton(CardBar bar)
        {
            ApplyAllMmOnBar(bar);
        }

        internal static void ApplyAllMmOnBar(CardBar bar)
        {
            if (bar == null) return;
            var t = bar.transform;
            for (var i = 0; i < t.childCount; i++)
            {
                var go = t.GetChild(i).gameObject;
                var button = go.GetComponent<CardBarButton>();
                if (button == null) continue;
                var card = CardField?.GetValue(button) as CardInfo;
                Apply(go, card);
            }
        }

        internal static void Apply(GameObject button, CardInfo card)
        {
            if (button == null || card == null) return;
            // FancyCardBar owns every other mod's icons. Touching those buttons
            // (disabling RGB overlays / TMP) blanked the whole bar.
            if (!IsMmCard(card)) return;

            CardArtFactory.TryAssignSprite(card);
            AttachFancyIcon(card);

            var sprite = SpriteFor(card);
            if (sprite == null) return;

            foreach (var tmp in button.GetComponentsInChildren<TMP_Text>(true))
            {
                tmp.enabled = false;
                tmp.text = "";
            }

            StripFancyOverlays(button);
            EnsureCleanOverlay(button, sprite);

            if (card.sprite == null)
                card.sprite = sprite;
        }

        internal static Sprite SpriteFor(CardInfo card)
        {
            if (card == null) return null;
            if (card.cardArt != null)
            {
                var tag = card.cardArt.GetComponent<MmCardArtTag>();
                if (tag != null && !string.IsNullOrEmpty(tag.ArtName))
                {
                    var mini = CardArtFactory.GetMiniSprite(tag.ArtName);
                    if (mini != null) return mini;
                }
            }

            return IsMmCard(card) ? card.sprite : null;
        }

        /// <summary>
        /// FancyCardBar looks for FancyIcon on the CardInfo GameObject and instantiates
        /// fancyIcon into the bar button — that skips the muddy "generate from cardArt" path.
        /// </summary>
        internal static void AttachFancyIcon(CardInfo info)
        {
            try
            {
                if (info == null || !IsMmCard(info)) return;
                var fancyType = FancyIconType();
                if (fancyType == null) return;

                string artName = null;
                if (info.cardArt != null)
                {
                    var tag = info.cardArt.GetComponent<MmCardArtTag>();
                    if (tag != null) artName = tag.ArtName;
                }

                if (string.IsNullOrEmpty(artName)) return;
                var prefab = GetOrCreateFancyPrefab(artName);
                if (prefab == null) return;

                var existing = info.gameObject.GetComponent(fancyType);
                if (existing == null)
                    existing = info.gameObject.AddComponent(fancyType);
                var field = AccessTools.Field(fancyType, "fancyIcon");
                field?.SetValue(existing, prefab);
            }
            catch (Exception ex)
            {
                Plugin.Instance?.LogWarn($"AttachFancyIcon skipped: {ex.Message}");
            }
        }

        private static Type FancyIconType()
        {
            if (_fancyResolved) return _fancyIconType;
            _fancyResolved = true;
            _fancyIconType = AccessTools.TypeByName("FancyCardBar.FancyIcon");
            return _fancyIconType;
        }

        private static GameObject GetOrCreateFancyPrefab(string artName)
        {
            if (FancyPrefabs.TryGetValue(artName, out var cached) && cached != null)
                return cached;

            var sprite = CardArtFactory.GetMiniSprite(artName);
            if (sprite == null) return null;

            // FancyCardBar Instantiates this prefab; inactive templates stay inactive.
            // Keep the icon GO active under a hidden inactive holder.
            if (!_fancyHolder)
            {
                _fancyHolder = new GameObject("MM_FancyBarIconHolder");
                UnityEngine.Object.DontDestroyOnLoad(_fancyHolder);
                _fancyHolder.SetActive(false);
            }

            var go = new GameObject(
                "MM_FancyBarIcon_" + artName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            go.transform.SetParent(_fancyHolder.transform, false);
            go.SetActive(true);

            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(128f, 128f);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;

            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.overrideSprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.color = Color.white;

            FancyPrefabs[artName] = go;
            return go;
        }

        private static void StripFancyOverlays(GameObject button)
        {
            if (button == null) return;

            foreach (var mb in button.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;
                if (mb.GetType().Name != "RGBColorShift") continue;
                mb.enabled = false;
                if (mb.gameObject != button)
                    mb.gameObject.SetActive(false);
            }

            for (var i = button.transform.childCount - 1; i >= 0; i--)
            {
                var child = button.transform.GetChild(i);
                if (child == null) continue;
                if (child.name == ChildName) continue;
                if (child.name.StartsWith("MM_FancyBarIcon_", StringComparison.Ordinal)) continue;

                if (child.GetComponentInChildren<MmCardArtTag>(true) != null)
                {
                    child.gameObject.SetActive(false);
                }
            }
        }

        private static void EnsureCleanOverlay(GameObject button, Sprite sprite)
        {
            var existing = button.transform.Find(ChildName);
            Image ours;
            if (existing == null)
            {
                var go = new GameObject(ChildName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                go.transform.SetParent(button.transform, false);
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.offsetMin = new Vector2(3f, 3f);
                rt.offsetMax = new Vector2(-3f, -3f);
                ours = go.GetComponent<Image>();
                ours.raycastTarget = false;
                ours.preserveAspect = true;
            }
            else
            {
                ours = existing.GetComponent<Image>();
                existing.SetAsLastSibling();
            }

            if (ours == null) return;
            ours.sprite = sprite;
            ours.overrideSprite = sprite;
            ours.color = Color.white;
            ours.enabled = true;
            ours.gameObject.SetActive(true);
        }
    }
}
