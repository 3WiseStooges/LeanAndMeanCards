"""
Curse-only roster management for Lean and Mean Cards.

The Steam IDs that are only ever offered curses must not appear in anyone's
BepInEx config, in this (public) repo, or as a readable string in the shipped
DLL -- the target reads their own config long before they think to decompile
anything. So CurseOnlyRoster.cs carries only an iterated, salted digest per ID,
and this script is the one place in the project that speaks plaintext.

    python tools/curse-only.py add   76561198000000000 -n "who it is"
    python tools/curse-only.py check 76561198000000000
    python tools/curse-only.py hash  76561198000000000
    python tools/curse-only.py list

The plaintext roster is tools/curse-only.local.txt, which is gitignored: it
stays on the dev machine that wrote it and nothing in the build reads it. A
clone without that file still builds and still curses the right people -- the
digests are what ship -- you just cannot read back who they are.

SALT and ROUNDS must stay identical to CurseOnlyRoster.cs or nothing matches.
"""
import argparse
import hashlib
import re
import sys
from pathlib import Path

SALT = "LeanAndMeanCards/curse-only/v1"
ROUNDS = 150000

ROOT = Path(__file__).resolve().parents[1]
ROSTER_CS = ROOT / "LeanAndMeanCards" / "Utils" / "CurseOnlyRoster.cs"
LOCAL = Path(__file__).resolve().parent / "curse-only.local.txt"

MARKER = "// curse-only:hashes"

# Individual Steam64 IDs: the account base plus a 32-bit account id.
STEAM64_BASE = 76561197960265728
STEAM64_MAX = STEAM64_BASE + 2 ** 32


def digest(steam_id):
    """One-way digest of an ID. Mirrors CurseOnlyRoster.Digest exactly."""
    h = hashlib.sha256("{}:{}".format(SALT, steam_id).encode("utf-8")).digest()
    for _ in range(ROUNDS - 1):
        h = hashlib.sha256(h).digest()
    return h.hex()


def parse_id(raw):
    """A Steam64 ID, or exit with the reason it is not one."""
    text = raw.strip()
    if not text.isdigit():
        sys.exit("'{}' is not a Steam64 ID (digits only).".format(raw))

    value = int(text)
    if not STEAM64_BASE < value < STEAM64_MAX:
        sys.exit(
            "{} is outside the individual-account range {}..{}. Steam profile URLs "
            "show this as the 17-digit number.".format(value, STEAM64_BASE + 1, STEAM64_MAX - 1)
        )

    return value


def source_hashes():
    """Every digest currently committed in CurseOnlyRoster.cs."""
    if not ROSTER_CS.exists():
        sys.exit("Roster source not found at {}".format(ROSTER_CS))

    text = ROSTER_CS.read_text(encoding="utf-8")
    start = text.find(MARKER)
    if start < 0:
        sys.exit("Marker '{}' missing from {}; add it back inside the Hashes array.".format(MARKER, ROSTER_CS))

    end = text.find("};", start)
    return re.findall(r'"([0-9a-f]{64})"', text[start:end])


def insert_hash(value):
    """Add one digest to the Hashes array, keeping the file's indentation."""
    text = ROSTER_CS.read_text(encoding="utf-8")
    marker_line_end = text.index("\n", text.index(MARKER))
    indent = re.match(r"[ \t]*", text[text.rindex("\n", 0, text.index(MARKER)) + 1:]).group(0)
    ROSTER_CS.write_text(
        text[:marker_line_end + 1] + '{}"{}",\n'.format(indent, value) + text[marker_line_end + 1:],
        encoding="utf-8",
    )


def local_entries():
    """(id, note) pairs from the gitignored plaintext roster."""
    if not LOCAL.exists():
        return []

    entries = []
    for line in LOCAL.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        steam_id, _, note = line.partition("#")
        entries.append((steam_id.strip(), note.strip()))

    return entries


def record_local(steam_id, note):
    if any(existing == str(steam_id) for existing, _ in local_entries()):
        return

    header = "" if LOCAL.exists() else (
        "# Plaintext curse-only roster. Gitignored -- this file is the only place\n"
        "# these IDs exist outside a dev's head. tools/curse-only.py keeps it.\n"
    )
    with LOCAL.open("a", encoding="utf-8") as handle:
        handle.write("{}{}  # {}\n".format(header, steam_id, note or "no note"))


def cmd_hash(args):
    print(digest(parse_id(args.steam_id)))


def cmd_check(args):
    steam_id = parse_id(args.steam_id)
    listed = digest(steam_id) in source_hashes()
    print("{} is {} the roster.".format(steam_id, "ON" if listed else "NOT on"))
    return 0 if listed else 1


def cmd_add(args):
    steam_id = parse_id(args.steam_id)
    value = digest(steam_id)
    if value in source_hashes():
        print("{} is already on the roster.".format(steam_id))
    else:
        insert_hash(value)
        print("Added {} to {}.".format(steam_id, ROSTER_CS.relative_to(ROOT)))

    record_local(steam_id, args.note)
    print("Plaintext recorded in {} (gitignored).".format(LOCAL.relative_to(ROOT)))
    return 0


def cmd_list(args):
    committed = source_hashes()
    known = set()

    entries = local_entries()
    if entries:
        for steam_id, note in entries:
            value = digest(int(steam_id)) if steam_id.isdigit() else None
            known.add(value)
            state = "on roster" if value in committed else "NOT IN SOURCE"
            print("{}  {:<14} {}".format(steam_id, state, note))
    else:
        print("No plaintext roster on this machine ({} is gitignored).".format(LOCAL.name))

    unknown = [h for h in committed if h not in known]
    if unknown:
        print(
            "\n{} committed digest(s) with no plaintext here: {}".format(
                len(unknown), ", ".join(h[:12] + "..." for h in unknown)
            )
        )

    return 0


def main():
    parser = argparse.ArgumentParser(description=__doc__.strip().splitlines()[0])
    subs = parser.add_subparsers(dest="command")

    for name, handler, help_text in (
        ("hash", cmd_hash, "print the digest for an ID"),
        ("check", cmd_check, "report whether an ID is on the committed roster"),
        ("add", cmd_add, "add an ID to the roster and to the local plaintext file"),
    ):
        sub = subs.add_parser(name, help=help_text)
        sub.add_argument("steam_id", help="17-digit Steam64 ID")
        sub.set_defaults(func=handler)
        if name == "add":
            sub.add_argument("-n", "--note", default="", help="who this is, for the local file")

    listing = subs.add_parser("list", help="show the local plaintext roster and any drift")
    listing.set_defaults(func=cmd_list)

    args = parser.parse_args()
    if not args.command:
        parser.print_help()
        return 2

    return args.func(args) or 0


if __name__ == "__main__":
    sys.exit(main())
