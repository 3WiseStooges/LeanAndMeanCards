using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LeanAndMeanCards.Utils
{
    /// <summary>
    /// Visual language borrowed from Pro MLG Stats: cool-gray glass panels,
    /// rounded 9-slice sprites, and low-contrast ghost buttons.
    /// </summary>
    internal static class UiGfx
    {
        private static Sprite _white;
        private static Sprite _rounded;

        internal const float TitleFont = 18f;
        internal const float HintFont = 13.5f;
        internal const float ButtonFont = 13f;
        internal const float TileFont = 14f;

        internal static readonly Color Accent = new Color(0.78f, 0.80f, 0.84f);
        internal static readonly Color Title = new Color(0.93f, 0.94f, 0.96f, 0.98f);
        internal static readonly Color Hint = new Color(0.84f, 0.87f, 0.92f, 0.96f);
        internal static readonly Color Panel = new Color(0.07f, 0.08f, 0.10f, 0.92f);
        internal static readonly Color Dim = new Color(0.02f, 0.03f, 0.05f, 0.72f);
        internal static readonly Color Tile = new Color(1f, 1f, 1f, 0.07f);
        internal static readonly Color TileSelected = new Color(1f, 1f, 1f, 0.16f);
        internal static readonly Color TileDisabled = new Color(1f, 1f, 1f, 0.03f);
        internal static readonly Color Label = new Color(0.86f, 0.88f, 0.91f, 0.95f);

        internal static Sprite White
        {
            get
            {
                if (_white != null) return _white;
                var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false)
                {
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Point
                };
                tex.SetPixel(0, 0, Color.white);
                tex.Apply(false, true);
                tex.hideFlags = HideFlags.HideAndDontSave;
                _white = Sprite.Create(tex, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
                _white.hideFlags = HideFlags.HideAndDontSave;
                return _white;
            }
        }

        internal static Sprite Rounded
        {
            get
            {
                if (_rounded != null) return _rounded;

                const int size = 64;
                const int radius = 14;
                var tex = new Texture2D(size, size, TextureFormat.ARGB32, false)
                {
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };

                var pixels = new Color[size * size];
                for (var y = 0; y < size; y++)
                {
                    for (var x = 0; x < size; x++)
                    {
                        pixels[y * size + x] = new Color(1f, 1f, 1f, RoundedAlpha(x + 0.5f, y + 0.5f, size, radius));
                    }
                }

                tex.SetPixels(pixels);
                tex.Apply(false, false);
                _rounded = Sprite.Create(
                    tex,
                    new Rect(0f, 0f, size, size),
                    new Vector2(0.5f, 0.5f),
                    100f,
                    0,
                    SpriteMeshType.FullRect,
                    new Vector4(radius, radius, radius, radius));
                _rounded.name = "LAMC_RoundedPanel";
                return _rounded;
            }
        }

        private static float RoundedAlpha(float x, float y, int size, int radius)
        {
            var innerLeft = radius;
            var innerRight = size - radius;
            var innerBottom = radius;
            var innerTop = size - radius;

            if (x >= innerLeft && x <= innerRight) return x >= 0f && x <= size && y >= 0f && y <= size ? 1f : 0f;
            if (y >= innerBottom && y <= innerTop) return x >= 0f && x <= size && y >= 0f && y <= size ? 1f : 0f;

            var cx = x < innerLeft ? innerLeft : innerRight;
            var cy = y < innerBottom ? innerBottom : innerTop;
            var dist = Vector2.Distance(new Vector2(x, y), new Vector2(cx, cy));
            return Mathf.Clamp01(radius - dist + 0.5f);
        }

        internal static Image Solid(Image image, Color color, bool raycast = false)
        {
            if (image == null) return null;
            image.sprite = White;
            image.type = Image.Type.Simple;
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        internal static Image Round(Image image, Color color, bool raycast = false)
        {
            if (image == null) return null;
            image.sprite = Rounded;
            image.type = Image.Type.Sliced;
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        internal static void GhostColors(Button button)
        {
            if (button == null) return;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 1f, 1f, 1f);
            colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            button.colors = colors;
        }

        internal static void StyleTmp(TextMeshProUGUI tmp, float size, Color color, TextAlignmentOptions align, bool wrap = true)
        {
            if (tmp == null) return;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = align;
            tmp.enableWordWrapping = wrap;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.raycastTarget = false;
            tmp.richText = true;
            tmp.lineSpacing = 0f;
        }

        internal static void AddAccentBar(Transform parent)
        {
            var accent = new GameObject("AccentTop", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            accent.transform.SetParent(parent, false);
            var rect = accent.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(0f, 3f);
            var le = accent.AddComponent<LayoutElement>();
            le.ignoreLayout = true;
            Solid(accent.GetComponent<Image>(), Accent);
        }
    }
}
