# Changelog

## 1.5.0

- **New rare card: Plummet.** Players you hit are spiked straight down until they
  smack the floor or the bottom of the map. One copy dives at 60 u/s — three times what the
  game counts as a hard landing — and every extra copy doubles it: 120, 240, 480, 960. Past
  two copies the drop takes a frame or two, so a short white streak follows the victim down
  and the landing kicks up more dust and shake per copy.
- The dive replaces the bullet's knockback rather than adding to it, and is swapped in at
  the same place the game decides knockback (`CallTakeForce` on the shooter's client), so
  every client sees the same slam. It holds the downward speed every physics tick instead of
  giving one push: drag, ramping gravity and a mid-air jump cannot slow it down.
- A target already on the ground is pinned — no push either way. A dive that reaches any
  map edge ends right there, so the edge gives its normal bounce and damage once instead of
  being hit again every tenth of a second by a victim still diving at 960 u/s.
- Bozo Shoes' +50% knockback applies to the dive speed. Your own reflected shots still push
  you the normal way.

## 1.4.0

- **Duplicate and Clone now ask after the pick phase, not during it.** Opening the chooser
  the moment the card was picked meant it could land on top of somebody else's picking turn
  and hold them there. It now runs from the pick-end hook, the way Genie's wish shop does:
  everyone has finished choosing cards, then the round waits up to **two minutes** (Genie's
  own window) for the copy to be chosen. Whoever is choosing sees a countdown in the panel;
  everyone else gets a banner naming who it is waiting for, with the same clock.
- Nothing is lost if the clock runs out. The master spends the copy on the best card in that
  player's hand and the round moves on, instead of leaving the debt pending and stalling the
  next pick end for another two minutes. Bots are resolved the same way, immediately and
  without any waiting — nobody can answer a chooser for them, so in 1.3.0 an AI that picked
  Duplicate simply got nothing.
- Every client decides when to stop waiting from the same synced state — who is owed a copy
  and still holds something copyable — so there is no handshake to race and no hold counter
  to get stuck. A player who is owed a copy but has nothing copyable is never waited for.
- **The chooser was rebuilt.** The old one put a ScrollRect inside a VerticalLayoutGroup,
  which drove the scroll rect's own transform and spilled the card rows over the buttons.
  Everything is explicitly placed now: title and countdown on one line, one card per row with
  a rarity colour chip and a count of how many you hold, a real scroll area, and two
  ordinary-sized buttons.
- Only copyable cards are listed. Greying out the rest made the list twice as long and asked
  the reader to work out why half of it was dead; a single line underneath now counts what
  was left out and why. Rarity moved from the card name's colour to a chip at the left edge —
  a common card's grey name read as disabled next to a rare card's blue one.
- **Yeet Cannon no longer kicks its own holder.** It is +100% bullet knockback and +15%
  damage; the self-knockback on firing, and the `Gun.Attack` patch behind it, are gone.
- **Takebacksies is Rare instead of Common**, and its category blacklist is re-asserted at
  every pick rather than once at game start. The blacklist is what keeps it out of the normal
  draw pool so it is only offered when you actually have a card to take back — a player who
  joined late, or whose stats were rebuilt by another mod, lost it and started drawing a
  *common* Takebacksies from the ordinary pool, which is why it turned up so often.

## 1.3.0

- **Two new cards that copy what you already have: Duplicate (rare) and Clone (legendary).**
  Both open a chooser over your own hand on your client only, and both hold the pick for the
  lobby while it is up, the way Thief and Sandbag do. Duplicate puts a second copy of the card
  you choose straight into your hand. Clone does that too, and then hands you another copy of
  the same card at the end of every round for the rest of the game.
- Neither card will copy the other, a Unique card, a curse, or a card whose whole effect is a
  one-shot prompt — Thief, Sandbag Simulator, Jar of Dirt and Return to Sender all track their
  use per player, so a second copy of one is a dead card. The chooser lists everything in your
  hand and writes the reason next to whatever it will not take. Thief cannot steal Duplicate or
  Clone either: a stolen copy card would pop its chooser on the thief's client in the middle of
  the steal, and Clone's target is tied to the player who chose it.
- Closing the chooser with **Later** costs nothing. How much a player is owed is derived from
  live state — Duplicates held minus Duplicates spent, Clones held minus Clones registered —
  rather than from a queue, so the prompt simply comes back at the next pick. The same
  arithmetic makes a double `ApplyStats` (Simulacrum) unable to leave a phantom copy behind,
  and losing a spent Duplicate and picking a fresh one later gives a working card instead of an
  inert one.
- The chooser opens synchronously from `OnAddCard`, not after a delay. UnboundLib runs
  `OnAddCard` as a *prefix* on `ApplyStats`, so the new card is not in `currentCards` yet and
  every count taken there is one short — but a delayed open is worse than a stale count: it can
  take the pick hold *after* the pick-end hook has stopped waiting, which drops the whole lobby
  into the round underneath the overlay. The owner's client asks, and the master re-checks the
  debt when the request lands seconds later, with every hand settled.
- Clone's round-end copies go through the same card-bar reconciliation the Silver Egg hatch
  needed. `AddCardToPlayer` puts a card on the bar that exists at that moment, and the bar is
  torn down between rounds and rebuilt, so a grant that lands at round end shows in the stats
  but never on the bar. `CardBarDebt` remembers what was handed out and re-asserts it at every
  pick and point start until the bar agrees with the hand.
- In offline mode the chooser only ever opens for the human. `PhotonView.IsMine` is true for
  every player there, so the plain `IsMine` check the rest of the pack uses would pop a card
  chooser over the host's screen when a *bot* picked Duplicate. The trade is that a bot that
  picks one of these two gets nothing from it — nobody is there to choose, and choosing for
  your opponent is worse than the card being quiet. Both are built for a real lobby.
- A deferred chooser only reopens on your own pick, like Thief's. It holds the lobby while it
  is up, so reopening it during somebody else's pick would stall a player who has nothing to
  do with it.
- Card art for both is placeholder: drawn procedurally in the pack's sticker language by
  `tools/make-copy-art.py` rather than painted like the rest. Dropping a replacement PNG in
  `Art/` under the same name is all it takes to swap.

## 1.2.10

- **TASER TASER TASER no longer tazes its own holder, and only the holder's hits taze.**
  Root cause, traced through a match log: KeysCards' **Spellbound** ("your bullets apply 3
  Hex") puts a `HexDebuffMono` on whoever the shot hits, and that component burns the
  victim's health once a second for six ticks with
  `healthHandler.TakeDamage(dmg, pos, null, player, ...)` — passing **the victim as
  `damagingPlayer`**. The taser keyed purely off who the damage was credited to, so every
  hex tick on a taser owner read as that owner landing a hit on themselves and stunned
  them: 0.5s of stun a second for six seconds, re-upped by the opponent's next hex shot.
  The opponent needed no stun card of their own, which is exactly how it was reported —
  their bullets alone, and nothing in their deck to explain it.

  The tick does not come through `DamageOverTime`, so the `healthRemoval` guard added in
  1.2.7 never saw it. The stun now requires the damage to be credited to a taser owner
  *and* the victim to be somebody else.

  Demonic Pact is the same bug on a second trigger, also confirmed in the assembly:
  `RootDemonicCards.PainfullAttacks` is a `DealtDamageEffect`, so vanilla runs it from
  inside `DoDamage` (`damagingPlayer.GetComponent<CharacterStatModifiers>().DealtDamage`),
  and it mirrors the damage with
  `healthHandler.DoDamage(damage, pos, magenta, null, self, false, true, true)` — the owner
  as both victim and `damagingPlayer`. So landing a hit tazed the owner through their own
  pact, once per hit, which against TASER's own +15% fire rate is near-permanent stun while
  shooting. The guard closes that, and the rest of the family with it: a reflected or homing
  shot of the owner's own, a blast of their own they stood in, an out-of-bounds kill
  credited to the player falling out. The card text no longer promises the old self-stun
  ("including catching your own shot").

  Bozo Shoes was never exposed — it has had the same-player check since 1.1.0.
- The stun's log line names both sides and the path it came through — `TASER stun shooter=1
  victim=0 src=hit` instead of `TASER stun player=0`. A line naming only the victim cannot
  tell an owner's hit from anybody else's, which was the one thing needed to settle this
  from a log. A suppressed self-stun logs too, at most once every 2s, so a repeat of this
  shape is one grep away.
- Both effects resolve the victim through `CharacterData.player` rather than walking the
  transform parents for a `Player`, so a reparented hit collider cannot credit the stun or
  the Bozo mark to the wrong player.

## 1.2.9

- Config files no longer hold on to settings this pack has stopped using. BepInEx
  keeps a key it cannot match to a `Bind` as an orphaned entry and writes it back
  out on every save, so a setting dropped from the code would otherwise sit in
  everyone's config forever. Launching 1.2.9 once clears the leftovers, currently
  the old `[Curse Only]` section.

## 1.2.8

- Diagnostics for the projectile-desync hunt. `[Diagnostics] Diagnostics` logs
  suppressed physics impulses and bounce-watchdog activity to the BepInEx log:
  enable it on every peer at once when reproducing a desync, then diff the logs.
  `[Diagnostics] FilterProjectileImpulses` is the A/B switch — true is the shipped
  behaviour, and false on every peer plays a match with the impulse filter out of
  the picture.

## 1.2.7

- **Silver Egg:** hatched cards now show in the top-right bar. The hatch still
  grants the cards at round end (so the stats are real), then the bar is
  restamped at pick / point start — that UI is torn down between rounds, which
  is why the icons never appeared. A toast names the haul.
- **Draft Sniper:** locking one card no longer makes the rest of the hand
  unpickable. 1.2.6 restored `pickrID` after a cancelled `Pick`, but a locked
  confirm still ran vanilla `pickrID = -1`, and mouse / other-mod picks never
  hit that postfix. A locked confirm is skipped now, and `pickrID` is put back
  from the tick as well.

## 1.2.6

- **Draft Sniper's LOCK buttons now exist during the pick they are meant to affect.**
  They were driven off `CardChoice.spawnedCards`, which is not a view of the offer for
  anyone except the picker: `Pick` gates `ReplaceCards` on the picker's own view being
  `IsMine`, so every other client's list stays empty until `RPCA_DoEndPick` back-fills it
  — and that RPC fires at the instant a card is chosen. The sniper is by definition not
  the picker, so the buttons were structurally impossible to show while the offer was
  live and appeared exactly when it stopped mattering. The card objects themselves are
  network-wide the whole time (`SpawnUniqueCard` goes through `PhotonNetwork.Instantiate`
  onto `CardChoice`'s own anchors), so `PickPhase.GetOfferedCards()` reads those instead
  and falls back to `spawnedCards` only when it is actually populated.
- **Locks are no longer rejected in lobbies where the host is not the picker.** The
  host-side `IsInOfferedHand` check in `RPCA_BanCard` read `spawnedCards` too, so it
  answered "That card is gone." for every lock in exactly the situation the card exists
  for.
- **A throwing `IDoEndPick` no longer takes the lobby with it.** `HoldEndPickPatch` pumps
  the original coroutine by hand with `while (original.MoveNext())`, which made this the
  one place any mod's end-of-pick work could fail — and a throw there means the next hand
  is never dealt. `MoveNext` is guarded now, and a broken pump completes the handoff
  itself, with `isPlaying` as the interlock against dealing two hands.
- Bounded the pick hold at 8s. `while (PickUiHold.ShouldWait)` was unbounded against an
  RPC-driven counter, so a dropped `Pop` was the same silent "nobody can pick" stall.
- `thunderstore.toml` was still on 1.2.4 after the 1.2.5 release; it tracks the real
  version again.

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
