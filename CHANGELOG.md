# Changelog

## 1.1.0

- **Curse-only accounts.** Steam IDs listed under `[Curse Only] SteamIds` are only ever
  offered curses. Defaults to one account; comma-separate for more, or clear it to disable.

  The check runs on the listed player's own machine, because a Steam ID never crosses the
  wire — ROUNDS puts only the Steam persona name on Photon. That works because the picker's
  own client builds their hand. It needs WillsWackyManagers for the curse pool, and steps
  aside when no curse is drawable so the offer cannot degrade into a hand of Nulls.

## 1.0.3

- Thunderstore listing matches the split, with links to
  [Mulligan Madness](https://thunderstore.io/c/rounds/p/LJIndustries/MulliganMadness/) and
  [Pro MLG Stats](https://thunderstore.io/c/rounds/p/LJIndustries/ProMLGStats/).

## 1.0.0

First release. These cards used to ship in Mulligan Madness; they are their own mod now.

- 14 cards: Confetti, Shove, Takebacksies, Bozo Shoes, Doorstop, Dynamite, Pisser, Draft Sniper,
  Safety Net, TASER TASER TASER, Yeet Cannon, Jar of Dirt, Sandbag Simulator, Thief.
- Bozo Shoes and TASER show the same stun and knockback for everyone in the lobby.
- Safety Net no longer quietly eats damage it shouldn't.
- Thief will not steal curses when Wills Wacky Managers is installed.
