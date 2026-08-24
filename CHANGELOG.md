# Changelog

## 1.2.0

- **Silver Egg** moves here from Mulligan Madness: hatches after 2 rounds into a small
  common/uncommon haul. The host rolls once and syncs the loot so remotes do not each
  get a different hand.
- Bozo Shoes sit lower on the feet, and no longer toast when someone is wearing them.
- Dynamite knocks harder and no longer fires the hit chromatic / screen-shake path.
  Blast knockback also skips bullets so host bounce trajectories are not flattened.
- Pick-card and mini-icon particle glow is killed for this pack's art (sticker outlines
  stay). Hold-to-shoot still works when Pisser is paired with Spray.

## 1.1.5

- Refresh the Thunderstore listing.

## 1.1.4

- **Fix missing card-bar mini icons.** Regression from 1.1.0's stamping coalescer: when a
  second card was added while a re-stamp was still in flight, the request was dropped
  entirely rather than queued. FancyCardBar and the vanilla bar rebuild their buttons a
  few frames after a card is added, so that card's icon was stamped once, immediately
  wiped by the rebuild, and never restored. Overlapping requests now queue one more pass
  instead of being discarded.

## 1.1.3

- **Dynamite's blast marker now matches the damage it does.** It was an opaque filled
  disc drawn at `sortingOrder 40` — in front of every player and bullet — and the
  dedupe guard was only 0.05 units within 0.08s, so a fast gun planted a separate
  marker per pellet. A dozen overlapping opaque discs in front of the fight is what
  engulfed the screen. It is now a hollow ring, translucent, at `sortingOrder 2`
  (behind players).
- The ring is authored so one sprite unit equals one world unit of blast: at scale 1
  its outer edge sits exactly on `BlastRadius`, the same circle `OverlapCircleAll`
  damages. It no longer grows during the fuse — urgency comes from brightness and
  alpha — so the ring never misrepresents what is about to be hit. Previously the
  drawn area peaked at about 42% of the real damage radius.
- Plants inside 60% of a blast radius of a charge that is already fusing now merge
  into it instead of stacking a new one. Anything that close would damage the same
  targets anyway.

## 1.1.2

- **Fix Pisser causing enormous damage on multi-projectile builds.** It set
  `gun.multiplySpread = 0` to mean "no spread". `ApplyCardStats.CopyGunStats`
  multiplies that field, and `Gun.GetShootDirection` uses it as
  `forward += cross * Random.Range(-spread, spread) * multiplySpread`, so zero
  pinned every projectile to one identical vector. On any shotgun-style build the
  whole volley stacked on a single point and landed on the same frame — damage
  times the projectile count, permanently, on a card meant to be a weak fast
  spray. It now halves spread instead.
- Pisser also set `gun.spread` and `gun.evenSpread` to 0. Those are *additive*
  in `CopyGunStats`, so adding 0 did nothing; removed.
- Pisser no longer writes spread fields straight onto the live gun in
  `OnAddCard`. That bypassed the card system and could not be undone if the card
  was later removed.
- Card text and stat readout now say "-50% spread" instead of "no spread".

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
