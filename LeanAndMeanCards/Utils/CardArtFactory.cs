using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace LeanAndMeanCards.Utils
{
    internal sealed class MmCardArtTag : MonoBehaviour
    {
        public string ArtName;
    }

    internal static class CardArtFactory
    {
        private static readonly HashSet<string> RegisteredCardNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> CardNameToArt =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private static string _artFolder;
        private static bool _artFolderResolved;

        private static string ArtFolder
        {
            get
            {
                if (_artFolderResolved) return _artFolder;
                _artFolderResolved = true;
                var dllDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
                var besideDll = Path.Combine(dllDir, "Art");
                if (Directory.Exists(besideDll)) { _artFolder = besideDll; return _artFolder; }
                var parent = Path.Combine(Directory.GetParent(dllDir)?.FullName ?? dllDir, "Art");
                if (Directory.Exists(parent)) { _artFolder = parent; return _artFolder; }
                _artFolder = besideDll;
                return _artFolder;
            }
        }

        private static readonly Dictionary<string, Sprite> FullSprites = new Dictionary<string, Sprite>();
        private static readonly Dictionary<string, Sprite> MiniSprites = new Dictionary<string, Sprite>();
        private static readonly Dictionary<string, GameObject> Templates = new Dictionary<string, GameObject>();

        internal static GameObject Create(string artName)
        {
            if (string.IsNullOrEmpty(artName)) return null;
            if (Templates.TryGetValue(artName, out var cached) && cached != null) return cached;

            var sprite = GetFullSprite(artName);
            if (sprite == null) return null;

            // Toggle Cards / card-bar hover / picks parent art under a UI RectTransform.
            // SpriteRenderer never draws there — use Unity UI Image (same pattern as MADGEIOS).
            // No Canvas on the template → invisible on the main menu until Instantiated onto a card.
            var root = new GameObject("MM_CardArt_" + artName, typeof(RectTransform));
            UnityEngine.Object.DontDestroyOnLoad(root);

            var tag = root.AddComponent<MmCardArtTag>();
            tag.ArtName = artName;

            var image = root.AddComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.color = Color.white;

            var rect = root.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;

            // Shake / moving bg / glow scale — see CardArtFx + CardVisualsFxPatch.
            CardArtFx.AttachToTemplate(root, artName);

            Templates[artName] = root;
            return root;
        }

        internal static Sprite GetMiniSprite(string artName)
        {
            if (string.IsNullOrEmpty(artName)) return null;
            if (MiniSprites.TryGetValue(artName, out var cached) && cached != null) return cached;

            var miniPath = Path.Combine(ArtFolder, artName + "_mini.png");
            var path = File.Exists(miniPath) ? miniPath : Path.Combine(ArtFolder, artName + ".png");
            if (!File.Exists(path)) return null;

            try
            {
                var texture = LoadTexture(path);
                if (texture == null) return null;
                var sprite = Sprite.Create(
                    texture,
                    new Rect(0f, 0f, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f),
                    100f);
                MiniSprites[artName] = sprite;
                return sprite;
            }
            catch
            {
                return null;
            }
        }

        internal static void TryAssignSprite(CardInfo info)
        {
            try
            {
                if (info == null) return;
                string artName = null;
                if (info.cardArt != null)
                {
                    var tag = info.cardArt.GetComponent<MmCardArtTag>();
                    if (tag != null) artName = tag.ArtName;
                }

                if (string.IsNullOrEmpty(artName) && !string.IsNullOrEmpty(info.cardName))
                    CardNameToArt.TryGetValue(info.cardName, out artName);

                if (string.IsNullOrEmpty(artName)) return;
                RegisterCard(info, artName);
                var mini = GetMiniSprite(artName);
                if (mini != null) info.sprite = mini;
                // Soft-dep FancyCardBar: custom bar icon prefab beats muddy auto-gen.
                CardBarMiniIcons.AttachFancyIcon(info);
            }
            catch (Exception ex)
            {
                // Called from CardInfo.Awake — never throw into Photon spawn.
                Plugin.Instance?.LogWarn($"TryAssignSprite skipped: {ex.Message}");
            }
        }

        internal static void RegisterCard(CardInfo info, string artName)
        {
            if (info == null || string.IsNullOrEmpty(artName)) return;
            if (!string.IsNullOrEmpty(info.cardName))
            {
                RegisteredCardNames.Add(info.cardName);
                CardNameToArt[info.cardName] = artName;
            }

            // Stamp the CardInfo itself so Photon / local clones still identify as ours
            // after cardArt loses the runtime tag.
            var tag = info.GetComponent<MmCardArtTag>();
            if (tag == null) tag = info.gameObject.AddComponent<MmCardArtTag>();
            if (string.IsNullOrEmpty(tag.ArtName)) tag.ArtName = artName;
        }

        internal static bool IsRegisteredCardName(string cardName)
        {
            return !string.IsNullOrEmpty(cardName) && RegisteredCardNames.Contains(cardName);
        }

        internal static string ArtNameFor(CardInfo info)
        {
            if (info == null) return null;
            var tag = info.GetComponent<MmCardArtTag>();
            if (tag != null && !string.IsNullOrEmpty(tag.ArtName)) return tag.ArtName;
            if (info.cardArt != null)
            {
                var artTag = info.cardArt.GetComponent<MmCardArtTag>();
                if (artTag != null && !string.IsNullOrEmpty(artTag.ArtName)) return artTag.ArtName;
            }

            if (info.sourceCard != null && info.sourceCard != info)
            {
                var fromSource = ArtNameFor(info.sourceCard);
                if (!string.IsNullOrEmpty(fromSource)) return fromSource;
            }

            if (!string.IsNullOrEmpty(info.cardName) && CardNameToArt.TryGetValue(info.cardName, out var mapped))
                return mapped;
            return null;
        }

        internal static void BindLoadedCardInfos()
        {
            foreach (var info in Resources.FindObjectsOfTypeAll<CardInfo>())
                TryAssignSprite(info);
        }

        private static Sprite GetFullSprite(string artName)
        {
            if (FullSprites.TryGetValue(artName, out var cached) && cached != null) return cached;

            var path = Path.Combine(ArtFolder, artName + ".png");
            if (!File.Exists(path))
            {
                // Older installs dropped PNGs next to the DLL.
                path = Path.Combine(
                    Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "",
                    artName + ".png");
            }

            if (!File.Exists(path)) return null;

            try
            {
                var texture = LoadTexture(path);
                if (texture == null) return null;
                var sprite = Sprite.Create(
                    texture,
                    new Rect(0f, 0f, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f),
                    100f);
                FullSprites[artName] = sprite;
                return sprite;
            }
            catch
            {
                return null;
            }
        }

        private static Texture2D LoadTexture(string path)
        {
            var bytes = File.ReadAllBytes(path);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(bytes))
            {
                UnityEngine.Object.Destroy(texture);
                return null;
            }

            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            UnityEngine.Object.DontDestroyOnLoad(texture);
            return texture;
        }
    }
}
