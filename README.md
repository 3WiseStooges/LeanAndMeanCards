# Lean and Mean Cards

Fourteen cards for ROUNDS. Mostly knockback, stuns and delayed blasts — plus a few that let you
reach across the table and mess with somebody else's pick.

Standalone. No card here depends on another mod being installed.

## Common

| | |
| :---: | --- |
| **Confetti** | +2 ammo, 25% faster fire, 10% less damage |
| **Shove** | +40% bullet knockback and +25% damage |
| **Takebacksies** | After someone steals from you, yoink that card back |

## Uncommon

| | |
| :---: | --- |
| **Bozo Shoes** | Players you hit wear clown shoes and take +50% knockback for the rest of the round |
| **Doorstop** | +1 block, block cooldown 20% longer |
| **Dynamite** | +20% damage. Bullets plant a small delayed blast on hit, including bounces. Weak boom, huge knockback. |
| **Pisser** | +4 ammo, 40% faster fire, no spread, 20% less damage |

## Rare

| | |
| :---: | --- |
| **Draft Sniper** | During someone else's pick, click a card to lock it so they can't take that one. Extra copies stack. |
| **Safety Net** | Map edges no longer deal damage. If you soft-lock outside the map, you die after a few seconds. |
| **TASER TASER TASER** | Hits stun for +0.5s, 15% faster fire, −1 ammo |
| **Yeet Cannon** | +100% bullet knockback, +15% damage, and your shots strongly kick you away from your gun |

## Legendary

| | |
| :---: | --- |
| **Sandbag Simulator** | Reroll someone's current pick hand (once per game) |
| **Thief** | Steal one card from another player (once per game) |

## Unique

| | |
| :---: | --- |
| **Jar of Dirt** | Only offered if you have Nulls. Converts those Nulls into treasures. Disabled Nulls stay Nulls. |

Toggle individual cards under **Toggle Cards → LeanAndMeanCards**.

## Optional integrations

All detected at runtime — none are required, and none are referenced at compile time.

- **RarityLib** — Legendary and Unique rarities. Without it those cards fall back to Rare.
- **FancyCardBar** — custom mini icons in the card bar.
- **TabInfo** — per-player card statuses in the Tab panel.
- **WillsWackyManagers** — Thief will not steal curses.
- **MulliganMadness** — Draft Sniper and Sandbag hold off while a Take All is being collected.

## Multiplayer

Everyone in the lobby needs this mod. Combat effects are applied by the host and broadcast once,
so all clients agree on stun durations and knockback.

## Build

```bash
dotnet build LeanAndMeanCards/LeanAndMeanCards.csproj -c Release
```

Override the paths if your install differs:

```bash
dotnet build LeanAndMeanCards/LeanAndMeanCards.csproj -c Release -p:RoundsFolder="D:\Steam\steamapps\common\ROUNDS" -p:R2ProfileName="MyProfile"
```

The DLL and `Art/` land in `package/`. Install and test through r2modman.
