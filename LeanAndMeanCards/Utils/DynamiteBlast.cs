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
        }

        internal static void KnockPhysics(Vector2 origin, Player owner)
        {
            var radiusSq = Dynamite.BlastRadius * Dynamite.BlastRadius;
            var seen = new HashSet<int>();
            var ownerData = owner != null ? owner.data : null;

            try
            {
                foreach (var npo in Object.FindObjectsOfType<NetworkPhysicsObject>())
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
            }
            catch
            {
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
            return false;
        }
    }

    internal sealed class DynamiteCharge : MonoBehaviour
    {
        private static Sprite _flash;
        private Player _owner;
        private bool _running;

        internal void Bind(Player owner) => _owner = owner;

        private void Start()
        {
            if (_running) return;
            _running = true;
            StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            var pulse = MakePulse();
            var elapsed = 0f;
            while (elapsed < Dynamite.BlastDelay)
            {
                elapsed += Time.deltaTime;
                var t = elapsed / Dynamite.BlastDelay;
                // Bomb fuse: slow blink early, frantic white flashes near boom.
                var hz = Mathf.Lerp(3.5f, 22f, t * t);
                var on = Mathf.PingPong(elapsed * hz, 1f) > 0.45f;
                var hot = t > 0.55f;

                if (pulse != null)
                {
                    // Scale stays at 1 = exactly the blast radius. Only a slight tick of
                    // breathing, so the ring never lies about what it is going to hit.
                    var scale = 1f + (on && hot ? 0.06f : 0f);
                    pulse.transform.localScale = new Vector3(scale, scale, 1f);

                    var sr = pulse.GetComponent<SpriteRenderer>();
                    if (sr != null)
                    {
                        // Urgency comes from brightness and alpha, not from growing.
                        var a = Mathf.Lerp(0.42f, 0.72f, t);
                        if (on)
                            sr.color = hot ? new Color(1f, 0.97f, 0.9f, a) : new Color(1f, 0.85f, 0.25f, a);
                        else
                            sr.color = new Color(1f, 0.12f, 0.08f, a * 0.7f);
                    }
                }

                yield return null;
            }

            Detonate();
            if (pulse != null) Object.Destroy(pulse);
            Object.Destroy(gameObject);
        }

        private void Detonate()
        {
            DynamiteBlast.PlayBoom(transform);
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

        private GameObject MakePulse()
        {
            var go = new GameObject("MM_DynamitePulse");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = FlashSprite();
            sr.color = new Color(1f, 0.18f, 0.12f, 0.55f);
            // Behind players and bullets. At 40 this painted over the whole fight.
            sr.sortingOrder = 2;
            go.transform.localScale = Vector3.one;
            return go;
        }

        /// <summary>
        /// A soft ring, not a filled disc.
        ///
        /// Built so one world unit of sprite equals one world unit of blast: the texture is
        /// authored at BlastRadius, so a localScale of 1 draws exactly the circle that
        /// OverlapCircleAll will damage. The old sprite was an opaque filled disc drawn at
        /// sortingOrder 40 — in front of every player — which is what buried the screen.
        /// </summary>
        private static Sprite FlashSprite()
        {
            if (_flash != null) return _flash;
            const int s = 128;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            var c = (s - 1) * 0.5f;
            var cream = new Color(1f, 0.93f, 0.82f, 1f);
            var red = new Color(0.93f, 0.12f, 0.14f, 1f);

            for (var y = 0; y < s; y++)
            {
                for (var x = 0; x < s; x++)
                {
                    var nx = (x - c) / c;
                    var ny = (y - c) / c;
                    var d = Mathf.Sqrt(nx * nx + ny * ny);

                    // Hollow: only the rim is drawn, so the play area stays readable.
                    var rim = Mathf.InverseLerp(0.62f, 0.94f, d) * Mathf.InverseLerp(1f, 0.94f, d);
                    if (d > 1f || rim <= 0.01f)
                    {
                        tex.SetPixel(x, y, Color.clear);
                        continue;
                    }

                    var col = Color.Lerp(red, cream, Mathf.InverseLerp(0.62f, 0.94f, d));
                    col.a = Mathf.Clamp01(rim);
                    tex.SetPixel(x, y, col);
                }
            }

            tex.Apply();
            Object.DontDestroyOnLoad(tex);

            // pixelsPerUnit = s / (2 * BlastRadius) => sprite diameter == blast diameter at scale 1.
            var ppu = s / (2f * Dynamite.BlastRadius);
            _flash = Sprite.Create(tex, new Rect(0f, 0f, s, s), new Vector2(0.5f, 0.5f), ppu);
            return _flash;
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
