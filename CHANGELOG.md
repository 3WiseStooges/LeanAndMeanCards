# Changelog

## 1.0.0

First release. Split out of MulliganMadness 0.3.31 as a standalone card pack.

- 14 cards, unchanged in behaviour: Confetti, Shove, Takebacksies, Bozo Shoes, Doorstop,
  Dynamite, Pisser, Draft Sniper, Safety Net, TASER TASER TASER, Yeet Cannon, Jar of Dirt,
  Sandbag Simulator, Thief.
- **Fixed double-applied combat effects.** Bozo Shoes and TASER broadcast from "master OR
  shooter", so a non-host shooter and the host both sent the same RPC on top of each client's
  own local application. Because AddStun is additive, a taser hit stunned for a different
  duration on every machine. The master is now the single authority.
- **Fixed a health desync risk in Safety Net.** Edge damage was suppressed in a prefix on the
  private DoDamage, which runs on every client from RPCA_SendTakeDamage. Suppression now
  happens only at CallTakeDamage, the point where the damage RPC is raised, so every client
  agrees. The out-of-bounds flag is also cleared by a Finalizer, so a throw mid-LateUpdate
  can no longer leave it stuck and suppress unrelated damage.
- No longer ships a stub `TabInfo.dll`. TabInfo integration is reflection-only and optional.
- Card bar icon stamping is coalesced. Five hooks reported the same card addition, so adding
  one card could trigger up to fifteen full-bar sweeps.
- WillsWackyManagers is now a true soft dependency (used only to avoid stealing curses).
