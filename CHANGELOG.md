# Changelog

## 1.2.5

- **TASER TASER TASER no longer re-tazes on every poison / Infernal tick.** The stun was
  applied from a `HealthHandler.DoDamage` postfix, and `DamageOverTime.DoDamageOverTime`
  calls `DoDamage` once per 0.25s interval with the *shooter* still set as
  `damagingPlayer`. One POISON or Infernal bullet therefore topped the stun back up for
  the whole burn — 219 stun applications in a single match's log. Damage-over-time ticks
  pass `healthRemoval: true`, which the postfix now checks, so the stun lands once for the
  hit that started the burn and never again.
- **Stop players being tazed by shots that never hit them.** Three separate causes:
  - The stun also fired from a `ProjectileHit.Hit` postfix. That is not a confirmed hit:
    `RayCastTrail.Update` runs on every client and calls `Hit` from that client's own
    prediction, before the blocked / already-hit checks decide anything, and only the
    bullet's owner turns it into an RPC. On the host, a mis-predicted local raycast then
    *broadcast* the phantom stun to everyone. The taser now applies only from
    `RPCA_DoHit` — the hit the shooter actually confirmed — and only when the shot was not
    blocked.
  - `ApplyStun` wrote `data.stunTime` directly and then reflected into the private
    `StunHandler.StartStun()`. That skipped the block check `AddStun` performs, zeroed the
    victim's velocity, and played the stun animation even on frames where `AddStun` had
    declined — a dazzle with no stun behind it. It now goes through `AddStun` alone.
  - The dedupe window was 0.2s against a 0.5s stun, so a burst kept re-upping it. The
    window is now the stun's own length.
- The taser no longer sends its own RPC. Both remaining call sites already run on every
  client for the same event (`RPCA_SendTakeDamage` and `RPCA_DoHit` are both
  `RpcTarget.All`), so the RPC only added a second, differently-timed copy of a stun that
  was already replicated. The dead `Gun.ApplyProjectileStats` hook that added to
  `ProjectileHit.stun` is gone too — nothing in the game reads that field.
- **Draft Sniper now has a LOCK button under each offered card.** It used to listen for a
  bare left click anywhere near a card, which raced the picker: locks landed after the card
  had already been taken, so the sniper got a "pick another" prompt for a pick that was
  over. Each offered card now carries its own button, drawn on the sniper's client only,
  that exists solely while the offer is live and reports its own state (`LOCK`,
  `LOCKING...`, `LOCKED`).
- **Fix Draft Sniper ending the picker's turn.** `CardChoice.DoPlayerSelect` runs
  `Pick(card); pickrID = -1;` without checking whether `Pick` did anything, and
  `CardChoice.Update` stops calling `DoPlayerSelect` once `pickrID` is -1. Cancelling a
  locked pick therefore retired the picker and left them staring at a hand they could no
  longer select from. The picker id is now restored after a cancelled pick.
- **Un-stick bullets that Target BOUNCE parked.** 1.2.1 stopped blasts flattening bounce
  shots, but the card has a second way to lose a bullet's speed that is all its own:
  `BounceEffectRetarget` switches the bullet off on contact and relies on the owner
  returning a new velocity 0.1s later over `ChildRPC`, with no fallback if that message
  never lands — the reported match's log is full of it being dropped ("Received RPC
  RPCA_RecieveFunction ... this PhotonView does not exist"). It also aims with
  `direction * move.velocity.magnitude`, so a zeroed magnitude resumes the bullet at a
  standstill. A watchdog now restores the plain reflected velocity if the bullet is still
  stopped well past the vanilla release point.
- Dynamite's blast no longer gives up on the rest of the scene when one map object throws.
  Map mods ship `NetworkPhysicsObject`s with no `PhotonView` — the same ones filling the
  log with their own `OnCollisionEnter2D` null references — and `RequestOwnership` throws
  on those. A single shared `try` around the whole loop meant the first bad object
  cancelled the knockback for every crate after it.

## 1.2.4

- Silver Egg no longer toasts when you pick it or when it hatches. Hatch still
  grants the cards, logs the names, and restamps the card bar so a hatched card
  like Bozo Shoes actually shows in the top right.
- BOZO sits higher above the health bar.
- Sandbag Simulator no longer dumps you into the fight while the prompt is
  open. The pick waits until you confirm or cancel. It queues Wills Wacky
  Managers' reroll instead of calling a method signature that does not exist
  (that is why nobody's cards changed). Local bots show color names instead of
  Player 1 / Player 2.
- Thief and Sandbag use the same glass panels, rounded tiles, and ghost
  buttons as Pro MLG Stats. Only the player who picked Thief can use it.
- Takebacksies just takes the card back. No toast.

## 1.2.3

- **Fix Null bar icons repeating the same mini PNG.** NullManager keeps every
  `NullCardInfo` on one GameObject, so the first Null we stamped wrote a shared art
  tag / FancyIcon that every later Null inherited. Null placeholders are no longer
  treated as Lean and Mean cards; leftover minis on those bar slots are cleared so
  NullManager's missing-texture icon can show.

## 1.2.2

- **Fix mini PNGs.** Pick-card art loaded because the full PNG search also looks next
  to the DLL, but mini icons only looked in an `Art/` folder. r2modman extracts those
  PNGs next to the DLL, so the card bar kept the two-letter labels and the pick-card
  corner kept the vanilla template icon (which still got the selected-card bloom).
  Minis now load from either layout, get assigned after Unbound finishes `BuildCard`,
  and the corner icon is color-locked like the main sticker.
- **Fix bouncing shots falling flat without Drop Grenade.** Dynamite already skipped its
  own blast on bullets, but vanilla explosions (Timed Detonation was in the local bots
  match) still shoved live projectiles on the host. Impulse pushes no longer hit bullets,
  and this pack's card templates pin `gun.gravity` to 1 so CopyGunStats cannot leak extra
  drop onto every LAMC card.

## 1.2.0

- **Silver Egg** moves here from Mulligan Madness: hatches after 2 rounds into a small
  common/uncommon haul. The host rolls once and syncs the loot so remotes do not each
  get a different hand.
- Bozo Shoes sit lower on the feet, and no longer toast when someone is wearing them.
- Dynamite knocks harder and no longer fires the hit chromatic / screen-shake path.
  Blast knockback also skips bullets so host bounce trajectories are not flattened.
- Pick-card bloom is killed for this pack's sticker art (selected cards no longer
  wash the PNG white). Mini-icon PNGs also stamp in local games when CardInfo clones
  drop art tags. Hold-to-shoot still works when Pisser is paired with Spray.

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
