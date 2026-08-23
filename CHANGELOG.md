# Changelog

## 1.1.1

- **Fix: no cards during the pick phase online.** Draft Sniper's guard on
  `CardChoice.Pick` cancelled the call whenever the card argument was null — but
  `StartPick` calls `Pick(null)` to *request a new offer*, not to select a card.
  `IsBlocked(null)` returned true, so the prefix returned false, `ReplaceCards`
  never ran and the hand was never built.

  The bug was latent before the 0.4.0 split: MulliganMadness used to cancel
  `Pick(null)` itself and start `ReplaceCards` by reflection, so this prefix's
  return value never mattered. Restoring vanilla spawning made it fatal.

- Cache the WillsWackyManagers curse type once. `Probe()` re-ran
  `AccessTools.TypeByName` on every call while `CurseManager.instance` was null,
  and that walks every loaded assembly calling `GetTypes()`. `IsCurse` runs once
  per card per offer slot, so a full card pool meant thousands of assembly scans.

- Cache the local-player lookup per frame in the curse-only check instead of
  scanning every player on every card evaluation.

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
