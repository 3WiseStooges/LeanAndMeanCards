using System.Collections;
using HarmonyLib;
using LeanAndMeanCards.Cards;
using Photon.Pun;
using Sonigon;
using UnboundLib.GameModes;
using UnityEngine;

namespace LeanAndMeanCards.Utils
{
    /// <summary>
    /// Plummet: a hit from a holder throws the victim straight down until they hit the
    /// floor or the bottom of the map.
    ///
    /// The dive is a velocity *hold*, not an impulse. Player velocity in ROUNDS is fought by
    /// air drag, ramping gravity and the legs' spring, so a one-off push would arrive at a
    /// different speed on every map and fade before it landed. Holding the downward speed
    /// every physics tick makes "60 u/s" mean 60 u/s, makes the stacking exact, and lets the
    /// victim's own jump do nothing until they touch down.
    ///
    /// Runs on every client. The shooter's client decides the hit and broadcasts it (see
    /// PlummetPatch), and each client then dives the victim locally — the same split vanilla
    /// uses for knockback (CallTakeForce → RPCA_SendTakeForce on every client). The victim's
    /// own client stays the position authority through SyncPlayerMovement.
    /// </summary>
    internal static class PlummetRuntime
    {
        /// <summary>One copy. Vanilla treats anything past 20 u/s down as a hard landing.</summary>
        internal const float BaseSpeed = 60f;

        /// <summary>Each extra copy multiplies the speed by this. 60, 120, 240, 480, 960…</summary>
        internal const float SpeedPerExtraCopy = 2f;

        /// <summary>
        /// Float sanity, not balance: 4000 u/s already crosses the whole map in one physics
        /// tick, so higher only risks precision trouble in the floor sweep.
        /// </summary>
        internal const float MaxSpeed = 4000f;

        /// <summary>Safety stop if neither the floor nor the edge ever shows up.</summary>
        internal const float MaxDiveSeconds = 1.5f;

        // OutOfBoundsHandler's play area: InverseLerp(-35.56, 35.56) and InverseLerp(-20, 20).
        internal const float MapHalfWidth = 35.56f;
        internal const float MapHalfHeight = 20f;

        private static readonly AccessTools.FieldRef<PlayerVelocity, Vector2> VelocityRef =
            AccessTools.FieldRefAccess<PlayerVelocity, Vector2>("velocity");

        private static readonly AccessTools.FieldRef<PlayerVelocity, bool> SimulatedRef =
            AccessTools.FieldRefAccess<PlayerVelocity, bool>("simulated");

        internal static void RegisterHooks()
        {
            GameModeManager.AddHook(GameModeHooks.HookGameStart, OnClear);
            GameModeManager.AddHook(GameModeHooks.HookPointStart, OnClear);
            GameModeManager.AddHook(GameModeHooks.HookPointEnd, OnClear);
            GameModeManager.AddHook(GameModeHooks.HookRoundEnd, OnClear);
        }

        internal static float SpeedFor(int copies)
        {
            if (copies <= 0) return 0f;
            return Mathf.Min(BaseSpeed * Mathf.Pow(SpeedPerExtraCopy, copies - 1), MaxSpeed);
        }

        internal static string StatusText(Player player)
        {
            var copies = CardOwnership.CountOf(player, Plummet.Card);
            if (copies <= 1) return "Hits spike straight down";
            var mult = Mathf.RoundToInt(SpeedFor(copies) / BaseSpeed);
            return "Hits spike down, " + mult + "x speed";
        }

        internal static Vector2 GetVelocity(PlayerVelocity vel) => vel != null ? VelocityRef(vel) : Vector2.zero;

        internal static void SetVelocity(PlayerVelocity vel, Vector2 value)
        {
            if (vel != null) VelocityRef(vel) = value;
        }

        internal static bool IsSimulated(PlayerVelocity vel) => vel != null && SimulatedRef(vel);

        internal static bool IsOutsideMap(Vector3 pos) =>
            pos.x <= -MapHalfWidth || pos.x >= MapHalfWidth || pos.y <= -MapHalfHeight || pos.y >= MapHalfHeight;

        /// <summary>Applied locally on every client from the broadcast.</summary>
        internal static void Begin(Player victim, float speed, int copies)
        {
            var data = victim != null ? victim.data : null;
            if (data == null || data.playerVel == null) return;
            if (data.dead || !data.isPlaying || !IsSimulated(data.playerVel)) return;
            if (speed <= 0f) return;

            var dive = victim.GetComponent<PlummetDive>() ?? victim.gameObject.AddComponent<PlummetDive>();
            dive.Begin(Mathf.Min(speed, MaxSpeed), Mathf.Max(1, copies));

            // Same grace vanilla gives a knockback (RPCA_SendTakeForce): a remote copy keeps
            // simulating the dive locally instead of lerping back to the last synced spot.
            try
            {
                var sync = victim.GetComponent<SyncPlayerMovement>();
                if (sync != null) sync.SetDontSyncFor(PhotonNetwork.GetPing() * 0.001f + 0.2f);
            }
            catch
            {
            }
        }

        /// <summary>
        /// Called from OutOfBoundsHandler.LateUpdate's prefix on every client. A dive that
        /// reaches any map edge ends there, so the edge gets an ordinary bounce instead of
        /// the hold fighting it — at stacked speeds vanilla's +200 push could not turn the
        /// victim around and they would eat the 51-damage edge hit every tenth of a second.
        /// </summary>
        internal static void CheckEdge(CharacterData data)
        {
            if (data == null) return;
            var dive = data.GetComponent<PlummetDive>();
            if (dive == null || !dive.Diving) return;
            if (!IsOutsideMap(data.transform.position)) return;
            dive.HitEdge();
        }

        internal static void Clear()
        {
            var players = PlayerManager.instance?.players;
            if (players == null) return;
            foreach (var player in players)
            {
                var dive = player != null ? player.GetComponent<PlummetDive>() : null;
                if (dive != null) dive.Abort();
            }
        }

        private static IEnumerator OnClear(IGameModeHandler gm)
        {
            Clear();
            yield break;
        }
    }

    internal sealed class PlummetDive : MonoBehaviour
    {
        private const string TrailName = "MM_PlummetTrail";

        private CharacterData _data;
        private PlayerCollision _collision;
        private CircleCollider2D _circle;
        private TrailRenderer _trail;

        private float _speed;
        private int _copies;
        private float _endTime;
        private float _startY;

        /// <summary>
        /// A drop shorter than this lands silently. Without it a fast gun on a target
        /// hopping along the floor turned the slam into a constant rumble.
        /// </summary>
        private const float MinSmackDrop = 1f;

        internal bool Diving { get; private set; }

        private void Awake()
        {
            _data = GetComponent<CharacterData>();
            _collision = GetComponent<PlayerCollision>();
            _circle = GetComponent<CircleCollider2D>();
        }

        internal void Begin(float speed, int copies)
        {
            if (_data == null) return;

            // A second hit mid-dive refreshes it; it never slows a faster dive down.
            _speed = Diving ? Mathf.Max(_speed, speed) : speed;
            _copies = Diving ? Mathf.Max(_copies, copies) : copies;
            _endTime = Time.time + PlummetRuntime.MaxDiveSeconds;

            // Already standing on something: there is nowhere further down to go, and the
            // shooter's side has already cancelled vanilla's push, so a grounded target is
            // simply pinned. Their own running speed is left alone — zeroing it here would
            // stop them dead on every bullet.
            if (_data.isGrounded && !Diving) return;

            if (!Diving)
            {
                _startY = transform.position.y;
                StartTrail();
            }

            Diving = true;
            Hold();

            // Covers the first tick: PlayerVelocity may move before this component's next
            // FixedUpdate.
            if (TrySnapToFloor(out var floor)) Land(floor);
        }

        private void FixedUpdate()
        {
            if (!Diving) return;

            if (_data == null || _data.dead || !_data.isPlaying || !PlummetRuntime.IsSimulated(_data.playerVel))
            {
                Abort();
                return;
            }

            if (PlummetRuntime.IsOutsideMap(transform.position))
            {
                HitEdge();
                return;
            }

            if (Time.time >= _endTime)
            {
                // Nothing to land on in time. Let go without leaving a comical-speed
                // fall behind.
                var v = PlummetRuntime.GetVelocity(_data.playerVel);
                v.y = Mathf.Max(v.y, -PlummetRuntime.BaseSpeed);
                PlummetRuntime.SetVelocity(_data.playerVel, v);
                Stop();
                return;
            }

            if (TrySnapToFloor(out var floor))
            {
                Land(floor);
                return;
            }

            // The owner's own anti-tunnel snap can park the body on the floor before the
            // sweep sees a gap to cross; the legs still report the ground.
            if (_data.isGrounded)
            {
                Land(default);
                return;
            }

            Hold();
        }

        /// <summary>Straight down: no sideways drift, and never slower than the dive speed.</summary>
        private void Hold()
        {
            var v = PlummetRuntime.GetVelocity(_data.playerVel);
            v.x = 0f;
            v.y = Mathf.Min(v.y, -_speed);
            PlummetRuntime.SetVelocity(_data.playerVel, v);
        }

        /// <summary>
        /// Sweeps the next tick's fall for a floor and parks the body on it.
        ///
        /// The owner's PlayerCollision already stops wall tunnelling, but remote copies run
        /// with checkForGoThroughWall off (SyncPlayerMovement.Awake), so at stacked speeds
        /// they would draw the victim straight through a platform until the next position
        /// sync. Sweeping here keeps every client's picture of the slam on the floor.
        /// </summary>
        private bool TrySnapToFloor(out RaycastHit2D floor)
        {
            floor = default;
            var pos = (Vector2)transform.position;
            var fall = Mathf.Max(-PlummetRuntime.GetVelocity(_data.playerVel).y, _speed);
            var dt = Time.fixedDeltaTime * Mathf.Max(TimeHandler.timeScale, 0.01f);
            // Extra margin for gravity added before the move and one frame of slack.
            var reach = (fall + 10f) * dt * 1.5f + 0.1f;

            // Under the 0.75 r that PlayerCollision lets a body sink into a wall, so a victim
            // sliding down a wall does not start the cast already overlapping it. Unity
            // reports a start overlap with a straight-up normal, which would read as a floor.
            var radius = BodyRadius() * 0.6f;
            int mask = _collision != null ? (int)_collision.mask : LayerMask.GetMask("Default");

            var hits = Physics2D.CircleCastAll(pos, radius, Vector2.down, reach, mask);
            var found = false;
            var best = float.MaxValue;
            foreach (var hit in hits)
            {
                if (hit.collider == null || hit.collider.isTrigger) continue;
                if (hit.transform.root == transform.root) continue;
                if (hit.distance <= 0f) continue;
                // Floors only. A wall brushed on the way down is not a landing.
                if (hit.normal.y < 0.35f) continue;
                if (hit.distance >= best) continue;
                best = hit.distance;
                floor = hit;
                found = true;
            }

            if (!found) return false;

            // Rest the full body on the contact, not the narrower probe circle.
            transform.position = new Vector3(pos.x, floor.point.y + BodyRadius() + 0.02f, 0f);
            return true;
        }

        private float BodyRadius()
        {
            var scale = Mathf.Abs(transform.localScale.x);
            return _circle != null ? _circle.radius * scale : 0.5f * scale;
        }

        private void Land(RaycastHit2D floor)
        {
            PlummetRuntime.SetVelocity(_data.playerVel, Vector2.zero);
            Stop();
            if (_startY - transform.position.y >= MinSmackDrop) Smack(floor);
        }

        internal void HitEdge()
        {
            if (!Diving) return;
            // Kill the dive's downward speed so the edge's own push throws the victim back in
            // like any other edge hit. Vanilla's damage and particles play as usual.
            var v = PlummetRuntime.GetVelocity(_data.playerVel);
            v.y = Mathf.Max(v.y, 0f);
            PlummetRuntime.SetVelocity(_data.playerVel, v);
            Stop();
        }

        /// <summary>End without touching velocity — point over, death, or a reset.</summary>
        internal void Abort()
        {
            Stop();
            if (_trail != null) _trail.Clear();
        }

        private void Stop()
        {
            Diving = false;
            if (_trail != null) _trail.emitting = false;
        }

        private void OnDisable() => Abort();

        /// <summary>
        /// The landing, played locally on every client. The dive zeroes vertical speed on
        /// contact, which keeps vanilla's TouchGround from seeing a hard landing, so its dust
        /// and thump are replayed here, a little bigger per copy.
        /// </summary>
        private void Smack(RaycastHit2D floor)
        {
            try
            {
                var shake = Mathf.Min(2f + _copies, 6f);
                if (GamefeelManager.instance != null) GamefeelManager.GameFeel(Vector2.down * shake);
            }
            catch
            {
            }

            try
            {
                var sound = _data.playerSounds != null ? _data.playerSounds.soundCharacterLandBig : null;
                if (sound != null && SoundManager.Instance != null) SoundManager.Instance.Play(sound, transform);
            }
            catch
            {
            }

            try
            {
                var parts = _data.landParts;
                if (parts == null) return;
                var normal = floor.collider != null ? (Vector3)floor.normal : Vector3.up;
                var y = floor.collider != null ? floor.point.y : transform.position.y - BodyRadius();
                // Vanilla's hardest landing is 0.5; each copy kicks up a bit more.
                var size = Mathf.Clamp(0.5f + 0.12f * (_copies - 1), 0.5f, 1f);
                foreach (var p in parts)
                {
                    if (p == null) continue;
                    p.transform.localScale = Vector3.one * size;
                    p.transform.position = new Vector3(transform.position.x, y, 5f);
                    p.transform.rotation = Quaternion.LookRotation(normal);
                    p.Play();
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// A short white streak behind the victim. At two or more copies the dive covers the
        /// drop in a frame or two, and without the streak it reads as a teleport rather than
        /// a slam.
        /// </summary>
        private void StartTrail()
        {
            if (_trail == null) _trail = BuildTrail();
            if (_trail == null) return;
            _trail.Clear();
            var scale = Mathf.Abs(transform.localScale.x);
            _trail.widthMultiplier = Mathf.Max(0.4f, scale) * 1.1f;
            _trail.emitting = true;
        }

        private TrailRenderer BuildTrail()
        {
            try
            {
                var existing = transform.Find(TrailName);
                var go = existing != null ? existing.gameObject : new GameObject(TrailName);
                go.transform.SetParent(transform, false);
                go.transform.localPosition = Vector3.zero;
                go.layer = gameObject.layer;

                var trail = go.GetComponent<TrailRenderer>() ?? go.AddComponent<TrailRenderer>();
                trail.time = 0.22f;
                trail.minVertexDistance = 0.05f;
                trail.startWidth = 1f;
                trail.endWidth = 0.15f;
                trail.numCapVertices = 4;
                trail.emitting = false;
                trail.autodestruct = false;

                var gradient = new Gradient();
                gradient.SetKeys(
                    new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.93f, 0.82f), 1f) },
                    new[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0f, 1f) });
                trail.colorGradient = gradient;

                var body = GetComponentInChildren<SpriteRenderer>(true);
                var shader = Shader.Find("Sprites/Default");
                if (shader != null) trail.material = new Material(shader);
                else if (body != null) trail.sharedMaterial = body.sharedMaterial;

                if (body != null)
                {
                    trail.sortingLayerName = body.sortingLayerName;
                    trail.sortingOrder = body.sortingOrder - 1;
                }

                return trail;
            }
            catch
            {
                return null;
            }
        }
    }
}
