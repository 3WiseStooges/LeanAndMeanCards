using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LeanAndMeanCards.Utils
{
    /// <summary>
    /// Per-card visual polish: vanilla PositionNoise shake, soft moving bg, glow scale.
    /// Wired onto cardArt templates and applied when CardVisuals mounts the instance.
    /// </summary>
    internal enum MmCardArtMotion
    {
        None = 0,
        SubtleShake = 1,
        EnergeticShake = 2,
        Jitter = 3,
    }

    internal sealed class MmCardArtFxTag : MonoBehaviour
    {
        public MmCardArtMotion Motion;
        public bool MovingBackground;
        /// <summary>Vanilla particle / border bloom scale. 0 = off (sticker PNGs wash out).</summary>
        public float GlowScale;
    }

    /// <summary>
    /// Sticker PNGs are already bright. CardVisuals.ChangeSelected tints every child
    /// Image with selectedColor (often HDR white / theme yellow), then the game bloom
    /// turns that into a halo that hides the art. Lock the sticker back to a
    /// below-bloom white every LateUpdate, including when Harmony misses a retint.
    /// </summary>
    internal sealed class MmArtColorLock : MonoBehaviour
    {
        internal static readonly Color ArtColor = new Color(0.82f, 0.82f, 0.82f, 1f);

        private Image _image;
        private SpriteRenderer _sr;

        private void Awake()
        {
            _image = GetComponent<Image>();
            _sr = GetComponent<SpriteRenderer>();
        }

        private void LateUpdate()
        {
            if (_image != null) _image.color = ArtColor;
            if (_sr != null) _sr.color = ArtColor;
        }
    }

    /// <summary>
    /// Soft drifting blobs behind sticker art — stands in for vanilla art-local
    /// GeneralParticleSystem backgrounds when we only ship PNGs (no asset bundle).
    /// Enabled/disabled with other CardAnimation components by CardVisuals.ChangeSelected.
    /// </summary>
    internal sealed class MmMovingCardBackground : CardAnimation
    {
        private const int BlobCount = 5;
        private Image[] _blobs;
        private Vector2[] _seeds;
        private Color _tint = new Color(1f, 0.55f, 0.15f, 0.05f);
        private bool _built;

        internal void SetTint(Color themeColor)
        {
            _tint = themeColor;
            _tint.a = 0.05f;
            if (_blobs == null) return;
            foreach (var img in _blobs)
            {
                if (img != null) img.color = _tint;
            }
        }

        private void OnEnable()
        {
            EnsureBuilt();
            SetBlobsVisible(true);
        }

        private void OnDisable()
        {
            SetBlobsVisible(false);
        }

        private void Update()
        {
            if (!_built || _blobs == null) return;
            var t = Time.unscaledTime;
            for (var i = 0; i < _blobs.Length; i++)
            {
                var img = _blobs[i];
                if (img == null) continue;
                var seed = _seeds[i];
                var rect = img.rectTransform;
                var x = Mathf.Sin((t + seed.x) * (0.35f + seed.y * 0.4f)) * 28f;
                var y = Mathf.Cos((t * 0.7f + seed.y) * (0.4f + seed.x * 0.35f)) * 22f;
                rect.anchoredPosition = new Vector2(x, y);
                var pulse = 0.85f + 0.15f * Mathf.Sin(t * (1.1f + seed.x) + seed.y);
                rect.localScale = Vector3.one * (0.7f + seed.x * 0.5f) * pulse;
                var c = _tint;
                c.a = _tint.a * (0.65f + 0.35f * pulse);
                img.color = c;
            }
        }

        private void EnsureBuilt()
        {
            if (_built) return;
            _built = true;
            _blobs = new Image[BlobCount];
            _seeds = new Vector2[BlobCount];

            // Sit behind the main sticker Image (factory adds Image on the root).
            for (var i = 0; i < BlobCount; i++)
            {
                var go = new GameObject("MM_BgBlob_" + i, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                go.transform.SetParent(transform, false);
                go.transform.SetAsFirstSibling();

                var rect = go.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(220f + i * 40f, 180f + i * 30f);
                rect.anchoredPosition = Vector2.zero;

                var img = go.GetComponent<Image>();
                img.sprite = SoftBlobSprite.Instance;
                img.type = Image.Type.Simple;
                img.preserveAspect = true;
                img.raycastTarget = false;
                img.color = _tint;

                _blobs[i] = img;
                _seeds[i] = new Vector2(0.15f + i * 0.17f, 0.4f + (i % 3) * 0.2f);
            }
        }

        private void SetBlobsVisible(bool on)
        {
            if (_blobs == null) return;
            foreach (var img in _blobs)
            {
                if (img != null) img.enabled = on;
            }
        }
    }

    internal static class SoftBlobSprite
    {
        private static Sprite _sprite;

        internal static Sprite Instance
        {
            get
            {
                if (_sprite != null) return _sprite;
                const int size = 64;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                var mid = (size - 1) * 0.5f;
                for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var dx = (x - mid) / mid;
                    var dy = (y - mid) / mid;
                    var d = Mathf.Sqrt(dx * dx + dy * dy);
                    var a = Mathf.Clamp01(1f - d);
                    a = a * a * (3f - 2f * a);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }

                tex.Apply(false, true);
                tex.filterMode = FilterMode.Bilinear;
                tex.wrapMode = TextureWrapMode.Clamp;
                Object.DontDestroyOnLoad(tex);
                _sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
                Object.DontDestroyOnLoad(_sprite);
                return _sprite;
            }
        }
    }

    internal static class CardArtFx
    {
        // Glow is off. Sticker PNGs + vanilla particles wash remotes to white.
        private static readonly Dictionary<string, FxSpec> Specs = new Dictionary<string, FxSpec>
        {
            ["dynamite"] = new FxSpec(MmCardArtMotion.EnergeticShake, movingBg: false, glow: 0f),
            ["yeetcannon"] = new FxSpec(MmCardArtMotion.EnergeticShake, movingBg: false, glow: 0f),
            ["kickback"] = new FxSpec(MmCardArtMotion.EnergeticShake, movingBg: false, glow: 0f),
            ["tasertasertaser"] = new FxSpec(MmCardArtMotion.Jitter, movingBg: false, glow: 0f),
            ["fumble"] = new FxSpec(MmCardArtMotion.Jitter, movingBg: false, glow: 0f),
            ["bozoshoes"] = new FxSpec(MmCardArtMotion.SubtleShake, movingBg: false, glow: 0f),
            ["shove"] = new FxSpec(MmCardArtMotion.SubtleShake, movingBg: false, glow: 0f),
            ["thief"] = new FxSpec(MmCardArtMotion.SubtleShake, movingBg: false, glow: 0f),
            ["confetti"] = new FxSpec(MmCardArtMotion.SubtleShake, movingBg: false, glow: 0f),
            ["returntosender"] = new FxSpec(MmCardArtMotion.None, movingBg: false, glow: 0f),
            ["silveregg"] = new FxSpec(MmCardArtMotion.None, movingBg: false, glow: 0f),
            ["nestegg"] = new FxSpec(MmCardArtMotion.None, movingBg: false, glow: 0f),
        };

        private const float DefaultGlow = 0f;

        // Must NOT be `readonly struct`: Unity's Mono lacks IsReadOnlyAttribute, and
        // Harmony PatchAll scans every type — one readonly struct aborts all MM patches.
        private struct FxSpec
        {
            internal MmCardArtMotion Motion;
            internal bool MovingBg;
            internal float Glow;

            internal FxSpec(MmCardArtMotion motion, bool movingBg, float glow)
            {
                Motion = motion;
                MovingBg = movingBg;
                Glow = glow;
            }
        }

        internal static void AttachToTemplate(GameObject root, string artName)
        {
            if (root == null || string.IsNullOrEmpty(artName)) return;

            var spec = Resolve(artName);
            var fx = root.GetComponent<MmCardArtFxTag>() ?? root.AddComponent<MmCardArtFxTag>();
            fx.Motion = spec.Motion;
            fx.MovingBackground = spec.MovingBg;
            fx.GlowScale = spec.Glow;
            if (root.GetComponent<MmArtColorLock>() == null)
                root.AddComponent<MmArtColorLock>();

            // Every MM card (including Art/ orphans not listed in Specs) gets a hard glow cut.
            if (!Specs.ContainsKey(artName))
                fx.GlowScale = DefaultGlow;

            ApplyMotion(root, spec.Motion);

            // Moving bg is opt-in only; default off so PNG stickers stay readable online.
            if (spec.MovingBg)
            {
                var bg = root.GetComponent<MmMovingCardBackground>()
                         ?? root.AddComponent<MmMovingCardBackground>();
                // Stay off on the DontDestroyOnLoad template; CardVisualsFxPatch re-enables on cards.
                bg.enabled = false;
            }
        }

        internal static float GlowScaleFor(MmCardArtFxTag fx)
        {
            // Default on: kill particle glow so sticker PNGs stay readable.
            if (Plugin.Configs == null || Plugin.Configs.SoftenCardGlow.Value) return 0f;
            return fx != null ? Mathf.Clamp(fx.GlowScale, 0f, 0.28f) : DefaultGlow;
        }

        private static FxSpec Resolve(string artName)
        {
            if (Specs.TryGetValue(artName, out var spec)) return spec;
            return new FxSpec(MmCardArtMotion.None, movingBg: false, glow: DefaultGlow);
        }

        private static void ApplyMotion(GameObject root, MmCardArtMotion motion)
        {
            var existing = root.GetComponent<PositionNoise>();
            if (motion == MmCardArtMotion.None)
            {
                if (existing != null) Object.Destroy(existing);
                return;
            }

            var noise = existing ?? root.AddComponent<PositionNoise>();
            // Canvas-local units; CardVisuals.ChangeSelected toggles CardAnimation enabled.
            noise.enabled = false;
            switch (motion)
            {
                case MmCardArtMotion.SubtleShake:
                    noise.amount = 4.5f;
                    noise.speed = 1.1f;
                    break;
                case MmCardArtMotion.EnergeticShake:
                    noise.amount = 9f;
                    noise.speed = 2.2f;
                    break;
                case MmCardArtMotion.Jitter:
                    noise.amount = 7f;
                    noise.speed = 4.5f;
                    break;
            }
        }
    }
}
