using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace LeanAndMeanCards.Utils
{
    /// <summary>
    /// Who curse-only applies to, as one-way digests.
    ///
    /// The IDs themselves are in none of the artefacts a player can reach: not the
    /// BepInEx config they open first, not this repo, and not as a readable string
    /// in the shipped DLL. A build can therefore answer "is this machine on the
    /// list", which is all it needs, while nothing shipped can answer "who is on it".
    ///
    /// tools/curse-only.py adds, checks and lists entries, and keeps the plaintext
    /// roster that lives only on a dev machine (gitignored). Its SALT and ROUNDS
    /// must match the constants here.
    /// </summary>
    internal static class CurseOnlyRoster
    {
        private static readonly string[] Hashes =
        {
            // curse-only:hashes — managed by tools/curse-only.py, do not hand-edit
            "34317146e6fe8fd88a891b51715b107d6bdec66ed5c818501b8e6e92c92d54ec",
        };

        // Namespaces the digests: the same ID hashed for any other purpose does not
        // collide with these, and a leak of one of these says nothing elsewhere.
        private const string Salt = "LeanAndMeanCards/curse-only/v1";

        // A Steam64 ID is a fixed base plus a 32-bit account id, so the whole plausible
        // ID space is about 2^31 candidates — a single SHA-256 would be a lunch-break
        // sweep for anyone who decompiles this, and the roster would be readable again.
        // Iterating multiplies that sweep by Rounds. The cost on this side is one digest
        // per session, primed at load so it never lands inside a pick.
        private const int Rounds = 150000;

        private static ulong _answeredFor;
        private static bool _answer;

        /// <summary>
        /// Whether <paramref name="steamId"/> is on the roster. Cached: the caller asks
        /// once per card per offer slot, and the answer costs 150k digests to reach.
        /// </summary>
        internal static bool Contains(ulong steamId)
        {
            if (steamId == 0UL || Hashes.Length == 0) return false;
            if (steamId == _answeredFor) return _answer;

            // A null digest means this platform has no SHA-256, which will not fix
            // itself — cache the "no" so the caller does not pay for the attempt again
            // on every card of every offer.
            var digest = Digest(steamId);

            var listed = false;
            if (digest != null)
            {
                foreach (var known in Hashes)
                {
                    if (string.Equals(known, digest, StringComparison.OrdinalIgnoreCase))
                    {
                        listed = true;
                        break;
                    }
                }
            }

            _answeredFor = steamId;
            _answer = listed;
            return listed;
        }

        /// <summary>Hex digest of one ID, or null if this platform has no SHA-256.</summary>
        private static string Digest(ulong steamId)
        {
            try
            {
                using (var sha = SHA256.Create())
                {
                    var hash = sha.ComputeHash(
                        Encoding.UTF8.GetBytes(Salt + ":" + steamId.ToString(CultureInfo.InvariantCulture)));
                    for (var i = 1; i < Rounds; i++) hash = sha.ComputeHash(hash);

                    var hex = new StringBuilder(hash.Length * 2);
                    foreach (var b in hash) hex.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                    return hex.ToString();
                }
            }
            catch (Exception ex)
            {
                Plugin.Instance?.LogWarn($"Roster digest unavailable: {ex.Message}");
                return null;
            }
        }
    }
}
