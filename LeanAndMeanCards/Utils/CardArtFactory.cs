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
        private static readonly string ArtFolder = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "",
            "Art");

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
            Object.DontDestroyOnLoad(root);

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

                if (string.IsNullOrEmpty(artName)) return;
                var mini = GetMiniSprite(artName);
                if (mini != null) info.sprite = mini;
                // Soft-dep FancyCardBar: custom bar icon prefab beats muddy auto-gen.
                CardBarMiniIcons.AttachFancyIcon(info);
            }
            catch (System.Exception ex)
            {
                // Called from CardInfo.Awake — never throw into Photon spawn.
                Plugin.Instance?.LogWarn($"TryAssignSprite skipped: {ex.Message}");
            }
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
                Object.Destroy(texture);
                return null;
            }

            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            Object.DontDestroyOnLoad(texture);
            return texture;
        }
    }
}
