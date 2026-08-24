using System.Collections;
using System.Collections.Generic;
using UnboundLib.GameModes;
using UnityEngine;

namespace LeanAndMeanCards.Utils
{
    internal static class BozoShoesRuntime
    {
        private const string VisualName = "MM_BozoMark";
        private static readonly HashSet<int> Marked = new HashSet<int>();
        private static Sprite _shoeSprite;
        private static Sprite _labelSprite;

        internal static void RegisterHooks()
        {
            GameModeManager.AddHook(GameModeHooks.HookGameStart, OnClear);
            GameModeManager.AddHook(GameModeHooks.HookRoundEnd, OnClear);
            GameModeManager.AddHook(GameModeHooks.HookPointStart, OnPointStart);
        }

        internal static void Clear()
        {
            Marked.Clear();
            StripAllVisuals();
        }

        internal static bool IsMarked(Player player) =>
            player != null && Marked.Contains(player.playerID);

        internal static void Mark(Player victim)
        {
            if (victim == null) return;
            Marked.Add(victim.playerID);
            AttachVisual(victim);
            EnsureTicker(victim);
        }

        private static IEnumerator OnClear(IGameModeHandler gm)
        {
            Clear();
            yield break;
        }

        private static IEnumerator OnPointStart(IGameModeHandler gm)
        {
            yield return null;
            ReapplyVisuals();
        }

        private static void ReapplyVisuals()
        {
            var players = PlayerManager.instance?.players;
            if (players == null) return;
            foreach (var player in players)
            {
                if (!IsMarked(player)) continue;
                AttachVisual(player);
                EnsureTicker(player);
            }
        }

        private static void StripAllVisuals()
        {
            var players = PlayerManager.instance?.players;
            if (players == null) return;
            foreach (var player in players)
            {
                StripVisual(player);
                var ticker = player != null ? player.GetComponent<BozoShoesTicker>() : null;
                if (ticker != null) Object.Destroy(ticker);
            }
        }

        private static void StripVisual(Player player)
        {
            if (player == null) return;
            var root = FindVisual(player);
            if (root != null) Object.Destroy(root);
        }

        private static GameObject FindVisual(Player player)
        {
            if (player == null) return null;
            var found = player.transform.Find(VisualName);
            return found != null ? found.gameObject : null;
        }

        private static void EnsureTicker(Player player)
        {
            if (player == null) return;
            if (player.GetComponent<BozoShoesTicker>() == null)
                player.gameObject.AddComponent<BozoShoesTicker>();
        }

        internal static void AttachVisual(Player player)
        {
            if (player == null) return;
            if (FindVisual(player) != null) return;

            var root = new GameObject(VisualName);
            root.transform.SetParent(player.transform, false);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;
            root.layer = player.gameObject.layer;

            var sort = 80;
            var layer = "Default";
            var bodySprite = player.GetComponentInChildren<SpriteRenderer>(true);
            if (bodySprite != null)
            {
                sort = bodySprite.sortingOrder + 40;
                if (!string.IsNullOrEmpty(bodySprite.sortingLayerName))
                    layer = bodySprite.sortingLayerName;
            }

            AddSprite(root.transform, "Label", LabelSprite(), Vector3.zero, 0f, Vector3.one, sort + 2, layer);
            AddSprite(root.transform, "ShoeL", ShoeSprite(), Vector3.zero, 22f, Vector3.one, sort, layer);
            AddSprite(root.transform, "ShoeR", ShoeSprite(), Vector3.zero, -22f, new Vector3(-1f, 1f, 1f), sort, layer);
            Pose(player, root);
        }

        internal static void Pose(Player player)
        {
            var root = FindVisual(player);
            if (root == null) return;
            Pose(player, root);
        }

        private static void Pose(Player player, GameObject root)
        {
            var origin = player.data?.playerVel != null
                ? player.data.playerVel.transform.position
                : player.transform.position;
            var size = 1f;
            if (player.data?.stats != null) size = Mathf.Max(0.45f, player.data.stats.sizeMultiplier);

            var head = origin + Vector3.up * (0.85f * size);

            var label = root.transform.Find("Label");
            var shoeL = root.transform.Find("ShoeL");
            var shoeR = root.transform.Find("ShoeR");

            if (label != null)
            {
                label.position = head + Vector3.up * (0.28f * size);
                label.rotation = Quaternion.identity;
                label.localScale = Vector3.one * (0.95f * size);
            }

            var feet = origin + Vector3.down * (0.92f * size);
            if (shoeL != null)
            {
                shoeL.position = feet + Vector3.left * (0.28f * size);
                shoeL.rotation = Quaternion.Euler(0f, 0f, 18f);
                shoeL.localScale = Vector3.one * (0.55f * size);
            }

            if (shoeR != null)
            {
                shoeR.position = feet + Vector3.right * (0.28f * size);
                shoeR.rotation = Quaternion.Euler(0f, 0f, -18f);
                shoeR.localScale = new Vector3(-0.55f * size, 0.55f * size, 1f);
            }
        }

        private static void AddSprite(Transform parent, string name, Sprite sprite, Vector3 localPos, float zRot, Vector3 localScale, int sort, string layer)
        {
            var go = new GameObject(name, typeof(SpriteRenderer));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(0f, 0f, zRot);
            go.transform.localScale = localScale;
            go.layer = parent.gameObject.layer;
            var sr = go.GetComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = sort;
            sr.sortingLayerName = layer;
            sr.color = Color.white;
        }

        private static Sprite ShoeSprite()
        {
            if (_shoeSprite != null) return _shoeSprite;
            const int w = 80;
            const int h = 48;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            var cream = new Color(1f, 0.93f, 0.82f, 1f);
            var red = new Color(0.95f, 0.12f, 0.18f, 1f);
            var yellow = new Color(1f, 0.86f, 0.12f, 1f);
            var cx = (w - 1) * 0.5f;
            var cy = (h - 1) * 0.46f;
            var rx = w * 0.46f;
            var ry = h * 0.40f;
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var nx = (x - cx) / rx;
                    var ny = (y - cy) / ry;
                    var d = nx * nx + ny * ny;
                    if (d > 1f) { tex.SetPixel(x, y, Color.clear); continue; }
                    Color c;
                    if (d > 0.78f) c = cream;
                    else if (ny > -0.12f && ny < 0.28f) c = yellow;
                    else c = red;
                    tex.SetPixel(x, y, c);
                }
            }

            tex.Apply();
            Object.DontDestroyOnLoad(tex);
            _shoeSprite = Sprite.Create(tex, new Rect(0f, 0f, w, h), new Vector2(0.5f, 0.5f), 48f);
            return _shoeSprite;
        }

        private static Sprite LabelSprite()
        {
            if (_labelSprite != null) return _labelSprite;

            const int w = 132;
            const int h = 36;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            var clear = Color.clear;
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                tex.SetPixel(x, y, clear);

            var yellow = new Color(1f, 0.88f, 0.12f, 1f);
            var black = new Color(0.05f, 0.04f, 0.04f, 1f);
            var cream = new Color(1f, 0.94f, 0.84f, 1f);

            // 5x7 block letters: B O Z O
            var glyphs = new[]
            {
                new[] { 0b11110, 0b10001, 0b11110, 0b10001, 0b10001, 0b10001, 0b11110 },
                new[] { 0b01110, 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b01110 },
                new[] { 0b11111, 0b00001, 0b00010, 0b00100, 0b01000, 0b10000, 0b11111 },
                new[] { 0b01110, 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b01110 }
            };

            const int scale = 3;
            const int gap = 4;
            var total = glyphs.Length * 5 * scale + (glyphs.Length - 1) * gap;
            var ox = (w - total) / 2;
            var oy = (h - 7 * scale) / 2;

            void Plot(int px, int py, Color c)
            {
                if (px < 0 || py < 0 || px >= w || py >= h) return;
                tex.SetPixel(px, py, c);
            }

            void Stamp(int gx, int gy, Color c)
            {
                for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                    Plot(gx + dx, gy + dy, c);
            }

            for (var gi = 0; gi < glyphs.Length; gi++)
            {
                var g = glyphs[gi];
                var bx = ox + gi * (5 * scale + gap);
                for (var row = 0; row < 7; row++)
                {
                    var bits = g[row];
                    for (var col = 0; col < 5; col++)
                    {
                        if ((bits & (1 << (4 - col))) == 0) continue;
                        for (var sy = 0; sy < scale; sy++)
                        for (var sx = 0; sx < scale; sx++)
                        {
                            var px = bx + col * scale + sx;
                            var py = oy + (6 - row) * scale + sy;
                            Stamp(px, py, cream);
                        }
                    }
                }
            }

            for (var gi = 0; gi < glyphs.Length; gi++)
            {
                var g = glyphs[gi];
                var bx = ox + gi * (5 * scale + gap);
                for (var row = 0; row < 7; row++)
                {
                    var bits = g[row];
                    for (var col = 0; col < 5; col++)
                    {
                        if ((bits & (1 << (4 - col))) == 0) continue;
                        for (var sy = 0; sy < scale; sy++)
                        for (var sx = 0; sx < scale; sx++)
                        {
                            var px = bx + col * scale + sx;
                            var py = oy + (6 - row) * scale + sy;
                            Plot(px, py, black);
                        }
                    }
                }
            }

            for (var gi = 0; gi < glyphs.Length; gi++)
            {
                var g = glyphs[gi];
                var bx = ox + gi * (5 * scale + gap);
                for (var row = 0; row < 7; row++)
                {
                    var bits = g[row];
                    for (var col = 0; col < 5; col++)
                    {
                        if ((bits & (1 << (4 - col))) == 0) continue;
                        for (var sy = 1; sy < scale - 1; sy++)
                        for (var sx = 1; sx < scale - 1; sx++)
                        {
                            var px = bx + col * scale + sx;
                            var py = oy + (6 - row) * scale + sy;
                            Plot(px, py, yellow);
                        }
                    }
                }
            }

            tex.Apply();
            Object.DontDestroyOnLoad(tex);
            _labelSprite = Sprite.Create(tex, new Rect(0f, 0f, w, h), new Vector2(0.5f, 0.5f), 32f);
            return _labelSprite;
        }
    }

    internal sealed class BozoShoesTicker : MonoBehaviour
    {
        private Player _player;

        private void Awake() => _player = GetComponent<Player>();

        private void LateUpdate()
        {
            if (_player == null) return;
            if (!BozoShoesRuntime.IsMarked(_player))
            {
                Destroy(this);
                return;
            }

            BozoShoesRuntime.AttachVisual(_player);
            BozoShoesRuntime.Pose(_player);
        }
    }
}
