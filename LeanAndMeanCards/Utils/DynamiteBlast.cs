using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using LeanAndMeanCards.Cards;
using Photon.Pun;
using Sonigon;
using SoundImplementation;
using UnboundLib.GameModes;
using UnityEngine;
using CardsApi = ModdingUtils.Utils.Cards;
using Object = UnityEngine.Object;

namespace LeanAndMeanCards.Utils
{
    internal static class DynamiteBlast
    {
        private const string EffectName = "MM_DynamiteCharge";
        private static SoundEvent _boom;
        private static bool _boomResolved;

        /// <summary>
        /// Set while the host applies blast knockback so Screenshaker / chromatic
        /// prefixes skip the hit-FX path. CallTakeDamage is not used for the blast.
        /// </summary>
        internal static bool SuppressHitFx;

        internal static void RegisterHooks()
        {
            GameModeManager.AddHook(GameModeHooks.HookGameStart, OnClearLive);
            GameModeManager.AddHook(GameModeHooks.HookPointEnd, OnClearLive);
            GameModeManager.AddHook(GameModeHooks.HookRoundEnd, OnClearLive);
        }

        internal static void Warmup()
        {
            ResolveBoom();
        }

        internal static void ApplyToGun(Gun gun)
        {
            // Kept for card SetupCard/OnAddCard call sites; hit patch does the work.
            ResolveBoom();
        }

        internal static void PlayBoom(Transform at)
        {
            ResolveBoom();
            if (_boom == null || at == null) return;
            try
            {
                var sm = SoundManager.Instance;
                if (sm == null) return;
                sm.Play(_boom, at);
            }
            catch
            {
            }
        }

        private static void ResolveBoom()
        {
            if (_boomResolved && _boom != null) return;
            _boomResolved = true;
            try
            {
                var gun = FindTimedDetonationGun();
                if (gun?.objectsToSpawn == null) return;
                foreach (var spawn in gun.objectsToSpawn)
                {
                    var effect = spawn?.effect;
                    if (effect == null) continue;

                    foreach (var explosion in effect.GetComponentsInChildren<Explosion>(true))
                    {
                        if (explosion?.soundDamage != null)
                        {
                            _boom = explosion.soundDamage;
                            return;
                        }
                    }

                    foreach (var player in effect.GetComponentsInChildren<SoundUnityEventPlayer>(true))
                    {
                        if (player == null) continue;
                        if (player.soundStart != null)
                        {
                            _boom = player.soundStart;
                            return;
                        }

                        if (player.soundEnd != null)
                        {
                            _boom = player.soundEnd;
                            return;
                        }
                    }
                }
            }
            catch
            {
            }
        }

        private static Gun FindTimedDetonationGun()
        {
            CardInfo info = null;
            try
            {
                info = CardsApi.instance?.GetCardWithName("Timed Detonation");
            }
            catch
            {
                info = null;
            }

            if (info == null)
            {
                var all = CardsApi.all;
                if (all != null)
                {
                    foreach (var card in all)
                    {
                        if (IsTimedDetonation(card))
                        {
                            info = card;
                            break;
                        }
                    }
                }
            }

            if (info == null)
            {
                foreach (var card in Resources.FindObjectsOfTypeAll<CardInfo>())
                {
                    if (!IsTimedDetonation(card)) continue;
                    info = card;
                    break;
                }
            }

            return info == null ? null : info.GetComponent<Gun>() ?? info.GetComponentInChildren<Gun>(true);
        }

        private static bool IsTimedDetonation(CardInfo card)
        {
            if (card == null) return false;
            if (!string.IsNullOrEmpty(card.cardName)
                && card.cardName.IndexOf("Timed Detonation", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            var objectName = card.gameObject != null ? card.gameObject.name : "";
            return objectName.IndexOf("TimedDetonation", StringComparison.OrdinalIgnoreCase) >= 0
                   || objectName.IndexOf("Timed Detonation", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static float _lastPlantTime;
        private static float _lastPlantLog;
        private static Vector3 _lastPlantPos;

        /// <summary>
        /// RPCA_DoHit already runs on every client. Plant locally on all of them so
        /// non-hosts see the fuse; combat applies on master only (see DynamiteCharge).
        /// </summary>
        internal static void TryPlantFromHit(ProjectileHit hit, Vector2 hitPoint, bool wasBlocked)
        {
            try
            {
                if (hit == null || wasBlocked) return;
                var owner = hit.ownPlayer;
                if (owner == null || !CardOwnership.Has(owner, Dynamite.Card)) return;

                var pos = hitPoint;
                if (pos == Vector2.zero && hit.transform != null) pos = hit.transform.position;

                // Merge into a charge that is already fusing nearby instead of stacking a
                // new one. The old guard was 0.05 units within 0.08s, which a fast gun blows
                // straight through: every pellet planted its own marker and the overlap is
                // what covered the screen. Anything inside roughly half a blast radius would
                // damage the same targets anyway, so a second charge there adds nothing but
                // noise.
                if (HasLiveChargeNear(pos, Dynamite.BlastRadius * 0.6f)) return;
                if ((pos - (Vector2)_lastPlantPos).sqrMagnitude < 0.05f && Time.time - _lastPlantTime < 0.08f) return;
                _lastPlantTime = Time.time;
                _lastPlantPos = pos;
                SpawnCharge(pos, owner);
                if (Time.time - _lastPlantLog > 2f)
                {
                    _lastPlantLog = Time.time;
                    Plugin.Instance?.Log(
                        $"Dynamite plant owner={owner.playerID} combatAuth={IsCombatAuthority()}");
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// True when a charge is already counting down within <paramref name="radius"/>.
        /// </summary>
        private static bool HasLiveChargeNear(Vector2 pos, float radius)
        {
            var rSq = radius * radius;
            foreach (var charge in Object.FindObjectsOfType<DynamiteCharge>())
            {
                if (charge == null) continue;
                if (((Vector2)charge.transform.position - pos).sqrMagnitude <= rSq) return true;
            }

            return false;
        }

        internal static void SpawnCharge(Vector3 position, Player owner)
        {
            var go = new GameObject(EffectName);
            go.transform.position = position;
            var charge = go.AddComponent<DynamiteCharge>();
            charge.Bind(owner);
        }

        internal static bool IsCombatAuthority()
        {
            return PhotonNetwork.OfflineMode || PhotonNetwork.IsMasterClient;
        }

        private static IEnumerator OnClearLive(IGameModeHandler gm)
        {
            ClearLive();
            yield break;
        }

        internal static void ClearLive()
        {
            foreach (var charge in Object.FindObjectsOfType<DynamiteCharge>())
            {
                if (charge != null) Object.Destroy(charge.gameObject);
            }

            foreach (var fx in Object.FindObjectsOfType<DynamiteBlastFx>())
            {
                if (fx != null) Object.Destroy(fx.gameObject);
            }
        }

        internal static void KnockPhysics(Vector2 origin, Player owner)
        {
            var radiusSq = Dynamite.BlastRadius * Dynamite.BlastRadius;
            var seen = new HashSet<int>();
            var ownerData = owner != null ? owner.data : null;

            // Guarded per object, not per loop. Map mods ship NetworkPhysicsObjects with no
            // PhotonView — the same ones filling the log with NREs out of their own
            // OnCollisionEnter2D — and RequestOwnership / RPCA_SendForce throw on those. One
            // shared try meant the first bad object in the scene cancelled the blast for
            // every crate after it.
            foreach (var npo in Object.FindObjectsOfType<NetworkPhysicsObject>())
            {
                try
                {
                    if (npo == null) continue;
                    if (IsProjectileBody(npo.transform)) continue;
                    var rb = npo.GetComponent<Rigidbody2D>() ?? npo.GetComponentInChildren<Rigidbody2D>();
                    var pos = rb != null ? rb.worldCenterOfMass : (Vector2)npo.transform.position;
                    var delta = pos - origin;
                    if (delta.sqrMagnitude > radiusSq) continue;
                    var dir = BlastDir(delta);
                    var push = dir * (Dynamite.BlastForce * 0.09f);
                    if (ownerData != null)
                    {
                        npo.RequestOwnership(ownerData);
                        npo.BulletPush(push, pos, ownerData);
                    }
                    else
                    {
                        npo.RPCA_SendForce(push, pos);
                    }

                    if (rb != null) seen.Add(rb.GetInstanceID());
                }
                catch
                {
                    // Skip this object, keep blasting the rest.
                }
            }

            var cols = Physics2D.OverlapCircleAll(origin, Dynamite.BlastRadius);
            if (cols == null) return;
            foreach (var col in cols)
            {
                if (col == null) continue;
                if (col.GetComponentInParent<Player>() != null) continue;
                if (IsProjectileBody(col.transform)) continue;
                var rb = col.attachedRigidbody;
                if (rb == null) rb = col.GetComponentInParent<Rigidbody2D>();
                if (rb == null || rb.bodyType != RigidbodyType2D.Dynamic) continue;
                if (IsProjectileBody(rb.transform)) continue;
                if (!seen.Add(rb.GetInstanceID())) continue;
                if (rb.GetComponentInParent<NetworkPhysicsObject>() != null) continue;

                var delta = rb.worldCenterOfMass - origin;
                var dir = BlastDir(delta);
                var view = rb.GetComponentInParent<PhotonView>();
                if (view != null && !view.IsMine)
                {
                    try { view.RequestOwnership(); }
                    catch { }
                }

                rb.WakeUp();
                rb.AddForce(dir * Mathf.Clamp(rb.mass * 28f, 18f, 160f), ForceMode2D.Impulse);
            }
        }

        private static Vector2 BlastDir(Vector2 delta)
        {
            var dir = delta.sqrMagnitude < 0.04f ? Vector2.up : delta.normalized;
            return (dir + new Vector2(0f, 0.55f)).normalized;
        }

        /// <summary>
        /// True for bullets and their MoveTransform / ProjectileHit hierarchy so blast
        /// knockback cannot flatten bounce trajectories on the host.
        /// </summary>
        internal static bool IsProjectileBody(Transform t)
        {
            if (t == null) return false;
            if (t.GetComponentInParent<ProjectileHit>() != null) return true;
            if (t.GetComponentInChildren<ProjectileHit>(true) != null) return true;
            if (t.GetComponentInParent<MoveTransform>() != null) return true;
            if (t.GetComponentInChildren<MoveTransform>(true) != null) return true;
            if (t.GetComponentInParent<RayHit>() != null) return true;
            if (t.GetComponentInChildren<RayHit>(true) != null) return true;
            return false;
        }
    }

    /// <summary>
    /// A planted charge: a small lit stick of dynamite sits where the bullet hit and
    /// blinks faster as the fuse runs down. The blast radius is only drawn when it goes off,
    /// as a full-size red circle.
    ///
    /// It used to draw the full blast-radius ring for the whole fuse, which on a fast gun
    /// meant several big rings on screen at all times.
    /// </summary>
    internal sealed class DynamiteCharge : MonoBehaviour
    {
        /// <summary>Stick length in world units, fuse not counted. A player is roughly 1.</summary>
        private const float StickLength = 0.55f;

        /// <summary>Tint on the fuse's "off" beats. Tints only darken, so the blink is a dip.</summary>
        private static readonly Color BlinkDim = new Color(0.55f, 0.45f, 0.45f, 1f);

        private Player _owner;
        private bool _running;
        private SpriteRenderer _bomb;
        private Transform _spark;

        internal void Bind(Player owner) => _owner = owner;

        private void Start()
        {
            if (_running) return;
            _running = true;
            StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            BuildBomb();
            var elapsed = 0f;
            while (elapsed < Dynamite.BlastDelay)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / Dynamite.BlastDelay);
                // Fuse: slow blink early, frantic flashes near boom.
                var hz = Mathf.Lerp(3.5f, 18f, t * t);
                var on = Mathf.PingPong(elapsed * hz, 1f) > 0.5f;

                if (_bomb != null)
                {
                    _bomb.color = on ? Color.white : Color.Lerp(Color.white, BlinkDim, 0.4f + 0.6f * t);
                    var s = 1f + (on ? 0.06f + 0.14f * t : 0f);
                    _bomb.transform.localScale = new Vector3(s, s, 1f);
                }

                if (_spark != null)
                {
                    var flick = UnityEngine.Random.Range(0.7f, 1.3f);
                    _spark.localScale = new Vector3(flick, flick, 1f);
                    _spark.localRotation = Quaternion.Euler(0f, 0f, UnityEngine.Random.Range(0f, 360f));
                }

                yield return null;
            }

            Detonate();
            Object.Destroy(gameObject);
        }

        private void BuildBomb()
        {
            var bomb = new GameObject("MM_DynamiteStick");
            bomb.transform.SetParent(transform, false);
            // Leaning like the card art, with a little wobble so a row of them does not
            // look stamped.
            bomb.transform.localRotation = Quaternion.Euler(0f, 0f, -22f + UnityEngine.Random.Range(-14f, 14f));
            _bomb = bomb.AddComponent<SpriteRenderer>();
            _bomb.sprite = DynamiteSprites.Stick(StickLength);
            // Small enough to sit in front of players without hiding anything, and a hit
            // on a body has to stay visible.
            _bomb.sortingOrder = 50;

            var spark = new GameObject("MM_DynamiteSpark");
            spark.transform.SetParent(bomb.transform, false);
            spark.transform.localPosition = DynamiteSprites.FuseTip(StickLength);
            var sr = spark.AddComponent<SpriteRenderer>();
            sr.sprite = DynamiteSprites.Spark(StickLength * 0.5f);
            sr.sortingOrder = 51;
            _spark = spark.transform;
        }

        private void Detonate()
        {
            DynamiteBlast.PlayBoom(transform);
            DynamiteBlastFx.Spawn(transform.position);
            if (!DynamiteBlast.IsCombatAuthority()) return;

            var origin = (Vector2)transform.position;
            DynamiteBlast.SuppressHitFx = true;
            try
            {
                var players = PlayerManager.instance?.players;
                if (players != null)
                {
                    foreach (var player in players)
                    {
                        if (player?.data?.healthHandler == null) continue;
                        if (_owner != null && player.teamID == _owner.teamID && player.playerID != _owner.playerID) continue;

                        var delta = (Vector2)player.transform.position - origin;
                        if (delta.sqrMagnitude > Dynamite.BlastRadius * Dynamite.BlastRadius) continue;

                        var dir = delta.sqrMagnitude < 0.04f ? Vector2.up : delta.normalized;
                        // Bias hard upward so grounded players actually leave the floor.
                        dir = (dir + new Vector2(0f, 0.85f)).normalized;
                        var force = dir * Dynamite.BlastForce;
                        force.y = Mathf.Max(force.y, Dynamite.BlastForce * 0.55f);
                        // Knockback only — CallTakeDamage is what drives chromatic aberration
                        // and the screen warp. Gun +20% damage is the card's damage stat.
                        player.data.healthHandler.CallTakeForce(
                            force,
                            ForceMode2D.Impulse,
                            true,
                            true,
                            Dynamite.BlastFlying);
                    }
                }

                DynamiteBlast.KnockPhysics(origin, _owner);
            }
            finally
            {
                DynamiteBlast.SuppressHitFx = false;
            }
        }
    }

    /// <summary>
    /// The boom: the full blast radius as a red circle, there at full size the instant it
    /// goes off, held a beat, then faded. A translucent fill so the area reads, and a
    /// solid rim on the exact edge OverlapCircleAll damages. Behind players, so it never
    /// buries the fight. Its own object, because the charge is destroyed the frame it
    /// detonates.
    /// </summary>
    internal sealed class DynamiteBlastFx : MonoBehaviour
    {
        private const float Hold = 0.12f;
        private const float Fade = 0.32f;
        private const float FillAlpha = 0.55f;

        private SpriteRenderer _fill;
        private SpriteRenderer _rim;
        private float _age;

        internal static void Spawn(Vector3 position)
        {
            var go = new GameObject("MM_DynamiteBlast");
            go.transform.position = position;
            go.AddComponent<DynamiteBlastFx>();
        }

        private void Awake()
        {
            // Scale stays 1: both sprites are authored at BlastRadius.
            _fill = Layer("Fill", DynamiteSprites.BlastFill(), 2);
            _rim = Layer("Rim", DynamiteSprites.BlastRim(), 3);
            Apply(1f);
        }

        private SpriteRenderer Layer(string name, Sprite sprite, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = order;
            return sr;
        }

        private void Update()
        {
            _age += Time.deltaTime;
            if (_age >= Hold + Fade)
            {
                Destroy(gameObject);
                return;
            }

            var k = _age <= Hold ? 1f : 1f - (_age - Hold) / Fade;
            Apply(k * k);
        }

        private void Apply(float strength)
        {
            if (_fill != null) _fill.color = new Color(1f, 1f, 1f, FillAlpha * strength);
            if (_rim != null) _rim.color = new Color(1f, 1f, 1f, strength);
        }
    }

    /// <summary>
    /// Procedural sprites for the dynamite stick, its fuse spark and the boom. Built once,
    /// cached. The blast fill and rim are authored at BlastRadius: localScale 1 draws
    /// exactly the circle OverlapCircleAll damages.
    /// </summary>
    internal static class DynamiteSprites
    {
        // Stick texture layout, in pixels, y up: one upright red stick, cream band round
        // its middle, rope fuse curling out of the top. Same read as the card's mini art.
        private const int StickTex = 96;
        private const float StickX0 = 32f, StickX1 = 60f, StickY0 = 8f, StickY1 = 72f;
        private const float StickR = 6f;
        private const float BandY0 = 34f, BandY1 = 46f;
        private const float CapRx = 14f, CapRy = 4.5f;
        private const float PivotX = (StickX0 + StickX1) * 0.5f, PivotY = (StickY0 + StickY1) * 0.5f;
        private const float TipX = 61f, TipY = 87f;
        private const float StickLengthPx = StickY1 - StickY0;

        private static Sprite _stick, _spark, _fill, _rim;
        private static float _stickSize, _sparkSize;

        private static readonly Color RedLit = new Color(1f, 0.42f, 0.36f, 1f);
        private static readonly Color Red = new Color(0.93f, 0.12f, 0.14f, 1f);
        private static readonly Color RedDark = new Color(0.58f, 0.05f, 0.08f, 1f);
        private static readonly Color RedTop = new Color(0.74f, 0.10f, 0.12f, 1f);
        private static readonly Color Hole = new Color(0.30f, 0.03f, 0.05f, 1f);
        private static readonly Color Gloss = new Color(1f, 0.80f, 0.76f, 1f);
        private static readonly Color BandLit = new Color(1f, 0.95f, 0.86f, 1f);
        private static readonly Color BandDark = new Color(0.84f, 0.74f, 0.58f, 1f);
        private static readonly Color Fuse = new Color(0.90f, 0.78f, 0.55f, 1f);
        private static readonly Color BlastRed = new Color(0.95f, 0.10f, 0.10f, 1f);
        private static readonly Color BlastRedHot = new Color(1f, 0.30f, 0.22f, 1f);
        private static readonly Color BlastRedDark = new Color(0.62f, 0.03f, 0.05f, 1f);

        /// <summary>Spark position relative to the stick's pivot, in world units.</summary>
        internal static Vector3 FuseTip(float length)
        {
            var ppu = StickLengthPx / length;
            return new Vector3((TipX - PivotX) / ppu, (TipY - PivotY) / ppu, 0f);
        }

        /// <param name="length">Stick length (not counting the fuse) in world units.</param>
        internal static Sprite Stick(float length)
        {
            if (_stick != null && Mathf.Approximately(_stickSize, length)) return _stick;
            _stickSize = length;
            var tex = StickerRaster.Supersampled(StickTex, StickTex, StickSample);
            _stick = Sprite.Create(tex, new Rect(0f, 0f, StickTex, StickTex),
                new Vector2(PivotX / StickTex, PivotY / StickTex), StickLengthPx / length);
            return _stick;
        }

        internal static Sprite Spark(float size)
        {
            if (_spark != null && Mathf.Approximately(_sparkSize, size)) return _spark;
            _sparkSize = size;
            const int s = 48;
            var tex = StickerRaster.Supersampled(s, s, SparkSample);
            _spark = Sprite.Create(tex, new Rect(0f, 0f, s, s), new Vector2(0.5f, 0.5f), s / size);
            return _spark;
        }

        /// <summary>Red disc filling the blast radius, a touch stronger toward the edge.</summary>
        internal static Sprite BlastFill()
        {
            if (_fill != null) return _fill;
            _fill = BlastCircle((d, edge) =>
            {
                var col = Color.Lerp(BlastRed, BlastRedHot, 1f - d);
                col.a = edge * Mathf.Lerp(0.7f, 1f, d * d);
                return col;
            });
            return _fill;
        }

        /// <summary>Solid red band on the very edge of the blast radius.</summary>
        internal static Sprite BlastRim()
        {
            if (_rim != null) return _rim;
            _rim = BlastCircle((d, edge) =>
            {
                // Inner edge of the band, softened over a couple of pixels.
                var band = Mathf.Clamp01((d - 0.9f) / 0.02f);
                if (band <= 0f) return Color.clear;
                var col = Color.Lerp(BlastRed, BlastRedDark, Mathf.InverseLerp(0.9f, 1f, d));
                col.a = band * edge;
                return col;
            });
            return _rim;
        }

        /// <summary>
        /// A circle exactly BlastRadius across at scale 1. shade gets the normalised
        /// distance from centre and the anti-aliased coverage of the outer edge.
        /// </summary>
        private static Sprite BlastCircle(System.Func<float, float, Color> shade)
        {
            const int s = 256;
            var c = s * 0.5f;
            var tex = StickerRaster.NewTexture(s, s);
            for (var y = 0; y < s; y++)
            for (var x = 0; x < s; x++)
            {
                var px = x + 0.5f - c;
                var py = y + 0.5f - c;
                var r = Mathf.Sqrt(px * px + py * py);
                var edge = Mathf.Clamp01(c - r);
                tex.SetPixel(x, y, edge <= 0f ? Color.clear : shade(Mathf.Min(r / c, 1f), edge));
            }

            tex.Apply();
            return Sprite.Create(tex, new Rect(0f, 0f, s, s), new Vector2(0.5f, 0.5f),
                s / (2f * Dynamite.BlastRadius));
        }

        private static Color StickSample(float x, float y)
        {
            var dStick = StickerRaster.RoundBoxDist(x, y, StickX0, StickY0, StickX1, StickY1, StickR);
            var dCap = StickerRaster.EllipseDist(x, y, PivotX, StickY1, CapRx, CapRy);
            var dFuse = FuseDist(x, y) - 1.8f;

            // Front to back: fuse, top face, its rim line, the stick's side, then the
            // shared ink outline and cream sticker rim under all of it.
            if (dFuse <= 0f) return Fuse;
            if (dCap <= 0f)
            {
                var hx = (x - PivotX) / 4.5f;
                var hy = (y - StickY1) / 1.8f;
                return hx * hx + hy * hy <= 1f ? Hole : RedTop;
            }

            if (dStick <= 0f)
            {
                if (dCap <= 1.3f) return StickerRaster.Ink;
                if (Mathf.Abs(y - BandY0) < 0.9f || Mathf.Abs(y - BandY1) < 0.9f) return StickerRaster.Ink;

                // Round stick lit from the left: light edge, red body, dark right side.
                var u = Mathf.InverseLerp(StickX0, StickX1, x);
                var band = y > BandY0 && y < BandY1;
                Color col;
                if (band)
                    col = Color.Lerp(BandLit, BandDark, u * u);
                else
                    col = u < 0.4f ? Color.Lerp(RedLit, Red, u / 0.4f) : Color.Lerp(Red, RedDark, (u - 0.4f) / 0.6f);
                if (!band && u > 0.16f && u < 0.27f && y > StickY0 + 6f && y < StickY1 - 6f)
                    col = Color.Lerp(col, Gloss, 0.6f);
                return col;
            }

            // Ink and a thin cream sticker rim, so the stick still reads on a red or dark map.
            return StickerRaster.Outline(Mathf.Min(dStick, Mathf.Min(dCap, dFuse)));
        }

        private static Color SparkSample(float x, float y)
        {
            const float c = 23.5f;
            var dx = x - c;
            var dy = y - c;
            var r = Mathf.Sqrt(dx * dx + dy * dy);
            if (r <= 5.5f) return Color.white;
            // Eight-point star: radius swings between 9 and 22 around the circle.
            var ang = Mathf.Atan2(dy, dx);
            var reach = Mathf.Lerp(9f, 22f, Mathf.Pow(Mathf.Abs(Mathf.Cos(ang * 4f)), 3f));
            if (r <= reach * 0.62f) return new Color(1f, 0.92f, 0.45f, 1f);
            if (r <= reach) return new Color(1f, 0.62f, 0.12f, 1f);
            return Color.clear;
        }

        /// <summary>Distance to the fuse: a short curl from the top of the stick to the spark.</summary>
        private static float FuseDist(float x, float y)
        {
            var best = float.MaxValue;
            var px = PivotX;
            var py = StickY1;
            const int steps = 10;
            for (var i = 1; i <= steps; i++)
            {
                var t = i / (float)steps;
                // Quadratic bezier: top of stick -> control -> tip.
                var u = 1f - t;
                var qx = u * u * PivotX + 2f * u * t * (PivotX - 3f) + t * t * TipX;
                var qy = u * u * StickY1 + 2f * u * t * (TipY + 3f) + t * t * TipY;
                best = Mathf.Min(best, StickerRaster.SegDist(x, y, px, py, qx, qy));
                px = qx;
                py = qy;
            }

            return best;
        }

    }

    [HarmonyPatch(typeof(ProjectileHit), "RPCA_DoHit")]
    internal static class DynamiteRpcHitPatch
    {
        private static void Postfix(ProjectileHit __instance, Vector2 hitPoint, bool wasBlocked)
        {
            DynamiteBlast.TryPlantFromHit(__instance, hitPoint, wasBlocked);
        }
    }

    /// <summary>
    /// CallTakeForce still pokes Screenshaker. Skip those calls while a Dynamite blast
    /// is applying knockback so the screen does not warp.
    /// </summary>
    [HarmonyPatch]
    internal static class DynamiteShakeSkipPatch
    {
        private static bool Prepare() => TargetMethods().GetEnumerator().MoveNext();

        private static IEnumerable<MethodBase> TargetMethods()
        {
            var names = new[] { "AddShake", "DoShake", "DoShakeObject", "ShakeInternal", "ScaleShake" };
            var types = new[] { "Screenshaker", "MenuEffects", "ObjectShake", "ChromaticAberration" };
            foreach (var typeName in types)
            {
                var t = AccessTools.TypeByName(typeName);
                if (t == null) continue;
                foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (m.IsSpecialName) continue;
                    foreach (var name in names)
                    {
                        if (m.Name == name) yield return m;
                    }
                }
            }
        }

        private static bool Prefix() => !DynamiteBlast.SuppressHitFx;
    }
}
