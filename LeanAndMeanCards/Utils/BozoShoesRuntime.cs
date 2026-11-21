using System.Collections;
using System.Collections.Generic;
using UnboundLib.GameModes;
using UnityEngine;

namespace LeanAndMeanCards.Utils
{
    /// <summary>
    /// Bozo Shoes marks: who is wearing clown shoes this point, and the shoes themselves.
    ///
    /// A mark lasts one point (one fight). It used to be cleared only at round end and
    /// re-applied at every point start, so a single hit kept the shoes and the +50%
    /// knockback on for the whole best-of-three.
    /// </summary>
    internal static class BozoShoesRuntime
    {
        internal const string LabelRootName = "MM_BozoMark";
        internal const string ShoeName = "MM_BozoShoe";

        private static readonly HashSet<int> Marked = new HashSet<int>();
        private static Sprite _shoeSprite;
        private static Sprite _labelSprite;

        internal static void RegisterHooks()
        {
            GameModeManager.AddHook(GameModeHooks.HookGameStart, OnClear);
            GameModeManager.AddHook(GameModeHooks.HookPointStart, OnClear);
            GameModeManager.AddHook(GameModeHooks.HookPointEnd, OnClear);
            GameModeManager.AddHook(GameModeHooks.HookRoundEnd, OnClear);
        }

        internal static void Clear()
        {
            Marked.Clear();
            var players = PlayerManager.instance?.players;
            if (players == null) return;
            foreach (var player in players)
            {
                var visual = player != null ? player.GetComponent<BozoShoesVisual>() : null;
                if (visual != null) Object.Destroy(visual);
            }
        }

        internal static bool IsMarked(Player player) =>
            player != null && Marked.Contains(player.playerID);

        internal static void Mark(Player victim)
        {
            if (victim == null) return;
            Marked.Add(victim.playerID);
            if (victim.GetComponent<BozoShoesVisual>() == null)
                victim.gameObject.AddComponent<BozoShoesVisual>();
        }

        private static IEnumerator OnClear(IGameModeHandler gm)
        {
            Clear();
            yield break;
        }

        /// <summary>
        /// The player's two feet, left one first, from the leg rig the game animates.
        /// Tries the drawn legs first (LegRenderer.end), then the IK targets behind them.
        /// Null when no rig is found; the caller falls back to a fixed pose.
        ///
        /// The player prefab draws its arms with LegRenderer too: ArmRendererLeft/Right
        /// run from the shoulder (Arm_Left/Right, x ±0.45) to the Hand, next to
        /// LegRendererLeft/Right from the hip (LegNew/Left/Right, x ±0.2) to the Foot.
        /// Taking the outermost LegRenderer pair therefore picked the hands, so each
        /// renderer is classified by the rig behind it and arms are dropped.
        /// </summary>
        internal static List<Transform> FindFeet(Player player)
        {
            if (player == null) return null;
            var feet = new List<KeyValuePair<Transform, float>>();
            var unclassified = new List<KeyValuePair<Transform, float>>();

            void AddTo(List<KeyValuePair<Transform, float>> into, Transform foot, Transform hip)
            {
                if (foot == null) return;
                foreach (var f in into)
                    if (f.Key == foot) return;
                // Sort on the hip, not the foot: feet cross mid-stride, hips do not.
                var side = hip != null ? hip.position.x : foot.position.x;
                into.Add(new KeyValuePair<Transform, float>(foot, side));
            }

            void Add(Transform foot, Transform hip) => AddTo(feet, foot, hip);

            foreach (var leg in player.GetComponentsInChildren<LegRenderer>(false))
            {
                if (leg == null) continue;
                switch (Classify(leg))
                {
                    case Limb.Leg:
                        Add(leg.end, leg.start);
                        break;
                    case Limb.Unknown:
                        AddTo(unclassified, leg.end, leg.start);
                        break;
                }
            }

            // A modded rig with no IK markers: use whatever was not recognisably an arm.
            if (feet.Count < 2 && unclassified.Count >= 2)
            {
                feet.Clear();
                feet.AddRange(unclassified);
            }

            if (feet.Count < 2)
            {
                feet.Clear();
                foreach (var leg in player.GetComponentsInChildren<IkLeg>(false))
                    if (leg != null) Add(leg.footTarget, leg.legRoot);
            }

            if (feet.Count < 2)
            {
                feet.Clear();
                foreach (var leg in player.GetComponentsInChildren<IKLegMove>(false))
                    if (leg != null) Add(leg.foot, leg.transform);
            }

            if (feet.Count < 2) return null;
            feet.Sort((a, b) => a.Value.CompareTo(b.Value));
            // Outermost pair if a mod gave the player extra legs (arms are already out).
            return new List<Transform> { feet[0].Key, feet[feet.Count - 1].Key };
        }

        private enum Limb
        {
            Leg,
            Arm,
            Unknown
        }

        /// <summary>
        /// Which limb a LegRenderer draws, from the components on its endpoints' parents.
        /// Legs hang off IkLeg / LegRotator (LegNew/Left/Rotator/Leg/Knee/Foot); arms off
        /// IKArmMove / ArmRotator (Arm_Left/Arm/Elbow/Hand). The rig's names are the
        /// last resort for mods that build their own limbs.
        /// </summary>
        private static Limb Classify(LegRenderer renderer)
        {
            var start = renderer.start;
            var end = renderer.end;
            if (end == null) return Limb.Arm;

            if (Under<IkLeg>(end) || Under<IkLeg>(start) || Under<LegRotator>(end) || Under<IKLegMove>(end))
                return Limb.Leg;
            if (Under<IKArmMove>(end) || Under<IKArmMove>(start) || Under<ArmRotator>(end) || Under<ArmRotator>(start))
                return Limb.Arm;

            if (Named(renderer.transform, "arm") || Named(end, "hand") || Named(start, "arm")) return Limb.Arm;
            if (Named(renderer.transform, "leg") || Named(end, "foot")) return Limb.Leg;
            return Limb.Unknown;
        }

        private static bool Under<T>(Transform t) where T : Component =>
            t != null && t.GetComponentInParent<T>() != null;

        private static bool Named(Transform t, string part) =>
            t != null && t.name.IndexOf(part, System.StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>
        /// Draw order for the marks: in front of the legs (or the shoe would sit behind the
        /// leg line it is meant to cover), and at least well in front of the body.
        /// </summary>
        internal static void SortFor(Player player, out string layer, out int order)
        {
            layer = "Default";
            order = 80;
            var body = player != null ? player.GetComponentInChildren<SpriteRenderer>(true) : null;
            if (body == null) return;
            if (!string.IsNullOrEmpty(body.sortingLayerName)) layer = body.sortingLayerName;
            order = body.sortingOrder + 40;

            foreach (var leg in player.GetComponentsInChildren<LegRenderer>(true))
            {
                if (leg == null) continue;
                foreach (var r in leg.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null || r.sortingLayerName != layer) continue;
                    order = Mathf.Max(order, r.sortingOrder + 2);
                }
            }
        }

        // Shoe texture layout, in pixels, y up. Side view, toe pointing right; the left
        // shoe is the same sprite mirrored.
        private const int ShoeW = 104, ShoeH = 60;
        private const float ToeX = 66f, ToeY = 27f, ToeRx = 30f, ToeRy = 19f;
        private const float HeelX = 28f, HeelY = 25f, HeelRx = 18f, HeelRy = 16f;
        private const float CuffX = 30f, CuffY = 40f;
        private const float SoleTop = 13f;
        /// <summary>Pivot: the bottom of the sole, under the ankle. Sits on the foot point.</summary>
        private const float AnkleX = 30f, SoleY = 8f;
        /// <summary>Toe tip to heel back, in pixels.</summary>
        private const float ShoeLengthPx = ToeX + ToeRx - (HeelX - HeelRx);

        private static readonly Color ShoeRedLit = new Color(1f, 0.40f, 0.36f, 1f);
        private static readonly Color ShoeRed = new Color(0.94f, 0.10f, 0.14f, 1f);
        private static readonly Color ShoeRedDark = new Color(0.56f, 0.04f, 0.08f, 1f);
        private static readonly Color ShoeGloss = new Color(1f, 0.82f, 0.78f, 1f);
        private static readonly Color SoleLit = new Color(1f, 0.95f, 0.86f, 1f);
        private static readonly Color SoleDark = new Color(0.80f, 0.70f, 0.56f, 1f);
        private static readonly Color CuffYellow = new Color(1f, 0.84f, 0.16f, 1f);
        private static readonly Color CuffDark = new Color(0.86f, 0.56f, 0.06f, 1f);
        private static readonly Color Opening = new Color(0.24f, 0.03f, 0.05f, 1f);

        /// <summary>One world unit long at scale 1, pivot on the sole under the ankle.</summary>
        internal static Sprite ShoeSprite()
        {
            if (_shoeSprite != null) return _shoeSprite;
            var tex = StickerRaster.Supersampled(ShoeW, ShoeH, ShoeSample);
            _shoeSprite = Sprite.Create(tex, new Rect(0f, 0f, ShoeW, ShoeH),
                new Vector2(AnkleX / ShoeW, SoleY / ShoeH), ShoeLengthPx);
            return _shoeSprite;
        }

        private static Color ShoeSample(float x, float y)
        {
            var dToe = StickerRaster.EllipseDist(x, y, ToeX, ToeY, ToeRx, ToeRy);
            var dHeel = StickerRaster.EllipseDist(x, y, HeelX, HeelY, HeelRx, HeelRy);
            var dMid = StickerRaster.RoundBoxDist(x, y, HeelX, SoleY + 1f, ToeX, 38f, 6f);
            var dShoe = Mathf.Min(dToe, Mathf.Min(dHeel, dMid));
            var dCuff = StickerRaster.EllipseDist(x, y, CuffX, CuffY, 13f, 6f);

            // Front to back: ankle cuff and its opening, then the shoe, then the shared
            // ink outline and cream rim.
            if (dCuff <= 0f)
            {
                if (StickerRaster.EllipseDist(x, y, CuffX, CuffY + 1f, 8.5f, 3.2f) <= 0f) return Opening;
                return Color.Lerp(CuffYellow, CuffDark, Mathf.InverseLerp(CuffX - 13f, CuffX + 13f, x) * 0.7f);
            }

            if (dShoe <= 0f)
            {
                if (dCuff <= 1.3f) return StickerRaster.Ink;
                if (Mathf.Abs(y - SoleTop) < 0.9f) return StickerRaster.Ink;
                if (y < SoleTop)
                    return Color.Lerp(SoleLit, SoleDark, Mathf.InverseLerp(HeelX - HeelRx, ToeX + ToeRx, x));

                // Lit from the upper left: bright top edge, deep red underneath.
                var lx = Mathf.InverseLerp(HeelX - HeelRx, ToeX + ToeRx, x);
                var ly = Mathf.InverseLerp(SoleTop, ToeY + ToeRy, y);
                var lit = Mathf.Clamp01(ly * 0.85f + (1f - lx) * 0.25f);
                var col = lit > 0.55f
                    ? Color.Lerp(ShoeRed, ShoeRedLit, (lit - 0.55f) / 0.45f * 0.8f)
                    : Color.Lerp(ShoeRedDark, ShoeRed, lit / 0.55f);
                if (StickerRaster.EllipseDist(x, y, ToeX + 4f, ToeY + 9f, 11f, 4.5f) <= 0f)
                    col = Color.Lerp(col, ShoeGloss, 0.7f);
                return col;
            }

            return StickerRaster.Outline(Mathf.Min(dShoe, dCuff));
        }

        internal static Sprite LabelSprite()
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

    /// <summary>
    /// The clown shoes and BOZO tag on one marked player. Destroying it takes the
    /// visuals with it, so clearing a mark is just Destroy(visual).
    ///
    /// Each shoe is parented to a foot transform from the leg rig, so it walks, steps and
    /// dangles with the feet the game animates. The old shoes were posed from the body's
    /// centre every frame, which glued them to the body instead of the feet.
    /// </summary>
    internal sealed class BozoShoesVisual : MonoBehaviour
    {
        /// <summary>Shoe length in world units at player scale 1. Comically big on purpose.</summary>
        private const float ShoeLength = 0.7f;

        private Player _player;
        private GameObject _labelRoot;
        private Transform _label;
        private readonly List<Transform> _shoes = new List<Transform>();
        private bool _onFeet;
        private float _nextRigTry;

        private void Awake() => _player = GetComponent<Player>();

        private void LateUpdate()
        {
            if (_player == null) return;
            if (!BozoShoesRuntime.IsMarked(_player))
            {
                Destroy(this);
                return;
            }

            if (_labelRoot == null || ShoesLost() || (!_onFeet && Time.time >= _nextRigTry))
                Build();
            Pose();
        }

        private void OnDestroy() => Teardown();

        private bool ShoesLost()
        {
            if (_shoes.Count == 0) return true;
            foreach (var shoe in _shoes)
                if (shoe == null) return true;
            return false;
        }

        private void Teardown()
        {
            if (_labelRoot != null) Destroy(_labelRoot);
            _labelRoot = null;
            _label = null;
            foreach (var shoe in _shoes)
                if (shoe != null) Destroy(shoe.gameObject);
            _shoes.Clear();
        }

        private void Build()
        {
            Teardown();
            BozoShoesRuntime.SortFor(_player, out var layer, out var order);

            _labelRoot = new GameObject(BozoShoesRuntime.LabelRootName);
            _labelRoot.transform.SetParent(_player.transform, false);
            _labelRoot.layer = _player.gameObject.layer;
            _label = Child(_labelRoot.transform, "Label", BozoShoesRuntime.LabelSprite(), layer, order + 2).transform;

            var feet = BozoShoesRuntime.FindFeet(_player);
            _onFeet = feet != null;
            if (_onFeet)
            {
                foreach (var foot in feet)
                    _shoes.Add(Child(foot, BozoShoesRuntime.ShoeName, BozoShoesRuntime.ShoeSprite(), layer, order).transform);
            }
            else
            {
                // No leg rig found (yet): pose under the body and look again shortly.
                _nextRigTry = Time.time + 0.5f;
                for (var i = 0; i < 2; i++)
                    _shoes.Add(Child(_labelRoot.transform, BozoShoesRuntime.ShoeName, BozoShoesRuntime.ShoeSprite(), layer, order).transform);
            }
        }

        private GameObject Child(Transform parent, string name, Sprite sprite, string layer, int order)
        {
            var go = new GameObject(name, typeof(SpriteRenderer));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.layer = _player.gameObject.layer;
            var sr = go.GetComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingLayerName = layer;
            sr.sortingOrder = order;
            sr.color = Color.white;
            return go;
        }

        private void Pose()
        {
            var unit = Mathf.Abs(_player.transform.lossyScale.x);
            if (unit < 0.01f) unit = 1f;
            var origin = _player.data?.playerVel != null
                ? _player.data.playerVel.transform.position
                : _player.transform.position;

            if (_label != null)
            {
                // Same spot and size the tag has always had.
                var size = _player.data?.stats != null ? Mathf.Max(0.45f, _player.data.stats.sizeMultiplier) : 1f;
                _label.position = origin + Vector3.up * (1.43f * size);
                _label.rotation = Quaternion.identity;
                SetWorldScale(_label, 0.95f * size * unit, 0.95f * size * unit);
            }

            for (var i = 0; i < _shoes.Count; i++)
            {
                var shoe = _shoes[i];
                if (shoe == null) continue;
                // Left shoe mirrored so both toes point outward.
                var side = i == 0 ? -1f : 1f;
                if (!_onFeet)
                    shoe.position = origin + new Vector3(side * 0.28f * unit, -0.92f * unit, 0f);
                else
                    shoe.localPosition = Vector3.zero;
                shoe.rotation = Quaternion.identity;
                var length = ShoeLength * unit;
                SetWorldScale(shoe, side * length, length);
            }
        }

        /// <summary>Sets lossy scale regardless of what the parent is scaled by.</summary>
        private static void SetWorldScale(Transform t, float x, float y)
        {
            var p = t.parent != null ? t.parent.lossyScale : Vector3.one;
            t.localScale = new Vector3(x / NonZero(p.x), y / NonZero(p.y), 1f);
        }

        private static float NonZero(float v) => Mathf.Abs(v) < 1e-4f ? (v < 0f ? -1e-4f : 1e-4f) : v;
    }
}
