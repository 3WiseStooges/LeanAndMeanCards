using System;
using System.Collections.Generic;
using System.Text;
using Photon.Pun;

namespace LeanAndMeanCards.Utils
{
    /// <summary>
    /// Session diagnostics for the projectile-desync hunt (see MulliganMadness.Utils.Diag; kept in sync by hand because the mods are separate assemblies).
    ///
    /// Per-event logging is useless at this volume: the reported match produced 1,389
    /// NetworkPhysicsObject collision faults in one session, which is what buried the six
    /// RPCA_DoHit faults that actually mattered. So every event is counted under a tag, a
    /// bounded number of samples per tag is kept for detail, and the set is flushed as a
    /// single line at each round boundary.
    ///
    /// Both peers write the same tags, so a desync shows up as a count that differs between
    /// the host log and the client log for the same round.
    /// </summary>
    internal static class Diag
    {
        private const int MaxSamplesPerTag = 3;

        private static readonly Dictionary<string, int> Counts = new Dictionary<string, int>();
        private static readonly Dictionary<string, List<string>> Samples = new Dictionary<string, List<string>>();
        private static readonly Dictionary<string, int> Totals = new Dictionary<string, int>();

        private static bool _enabled;
        private static bool _cached;

        /// <summary>
        /// Cached because the guards that call this sit on Unity physics callbacks that fire
        /// thousands of times a second; re-reading a ConfigEntry there would itself be a
        /// measurable cost. Refreshed at every round boundary, which is soon enough for a
        /// toggle that is flipped between matches.
        /// </summary>
        internal static bool Enabled
        {
            get
            {
                if (!_cached) Refresh();
                return _enabled;
            }
        }

        internal static void Refresh()
        {
            try
            {
                _enabled = Plugin.Configs?.Diagnostics?.Value ?? false;
            }
            catch
            {
                _enabled = false;
            }

            _cached = true;
        }

        /// <summary>host / client / offline, plus actor number, so two logs can be lined up.</summary>
        internal static string Peer
        {
            get
            {
                try
                {
                    if (PhotonNetwork.OfflineMode) return "offline";
                    var role = PhotonNetwork.IsMasterClient ? "host" : "client";
                    return role + "#" + (PhotonNetwork.LocalPlayer?.ActorNumber ?? -1);
                }
                catch
                {
                    return "unknown";
                }
            }
        }

        internal static void Count(string tag) => Event(tag, null);

        /// <summary>Record one occurrence of <paramref name="tag"/>, keeping the first few details.</summary>
        internal static void Event(string tag, string detail)
        {
            if (!Enabled || string.IsNullOrEmpty(tag)) return;

            try
            {
                Counts.TryGetValue(tag, out var n);
                Counts[tag] = n + 1;

                Totals.TryGetValue(tag, out var t);
                Totals[tag] = t + 1;

                if (string.IsNullOrEmpty(detail)) return;

                if (!Samples.TryGetValue(tag, out var list))
                {
                    list = new List<string>();
                    Samples[tag] = list;
                }

                if (list.Count < MaxSamplesPerTag) list.Add(detail);
            }
            catch
            {
                // Diagnostics must never be able to break a round.
            }
        }

        /// <summary>
        /// Emit everything counted since the last flush, then reset. Called at round
        /// boundaries; safe to call when nothing was counted (it stays silent).
        /// </summary>
        internal static void Flush(string reason)
        {
            if (!Enabled) return;

            try
            {
                if (Counts.Count == 0) return;

                var sb = new StringBuilder();
                sb.Append("DIAG ").Append(reason).Append(" peer=").Append(Peer).Append(" |");

                foreach (var pair in Counts)
                {
                    sb.Append(' ').Append(pair.Key).Append('=').Append(pair.Value);
                }

                Plugin.Instance?.Log(sb.ToString());

                foreach (var pair in Samples)
                {
                    foreach (var sample in pair.Value)
                    {
                        Plugin.Instance?.Log("DIAG   " + pair.Key + ": " + sample);
                    }
                }

                Counts.Clear();
                Samples.Clear();
            }
            catch
            {
            }
        }

        /// <summary>Cumulative totals for the whole session, for the end-of-game line.</summary>
        internal static void FlushTotals(string reason)
        {
            if (!Enabled) return;

            try
            {
                if (Totals.Count == 0) return;

                var sb = new StringBuilder();
                sb.Append("DIAG TOTALS ").Append(reason).Append(" peer=").Append(Peer).Append(" |");

                foreach (var pair in Totals)
                {
                    sb.Append(' ').Append(pair.Key).Append('=').Append(pair.Value);
                }

                Plugin.Instance?.Log(sb.ToString());
            }
            catch
            {
            }
        }

        internal static string Describe(UnityEngine.Object obj)
        {
            try
            {
                return obj == null ? "<null>" : obj.name;
            }
            catch
            {
                return "<err>";
            }
        }
    }
}
