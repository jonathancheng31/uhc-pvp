# Fishing rod

Our own rod, designed by the owner (2026-10-21) and inspired by 1.8 PvP rods. Its flight and
knockback numbers start from 1.8.9's (`EntityFishHook`, plus the 1.8 server software Hypixel
runs, which is where rod knockback on players comes from). Numbers live in
`scripts/Core/Tuning.cs` under "Fishing rod".

## How it plays

- **Right-click throws the rod**, any time: it's always in your other hand, with no slot
  switching and no cooldown. The sword stays in your main hand.
- **Right-click again** and the old bobber vanishes while a new one flies out. There's no
  separate reel-in.
- **A bobber that hits an enemy catches them:** it knocks them back away from you and hangs on
  their chest. No damage, no pull.
- **It counts as a hit for hit protection** (the owner's choice, like 1.8):
  - A rod hit starts the victim's half-second protection, with 0 as the "last damage".
  - A sword hit inside that half second is a stronger hit: full damage, but no knockback.
  - A rod hit on someone still protected doesn't catch them; the bobber flies on through.
- Teammates can't be caught (friendly fire is off).
- **The bobber disappears** when you throw again, when you die, when it's more than 32 blocks
  from you, or when it falls out of the world. If the player it hangs on dies, it drops.

## Numbers (Minecraft units: blocks and blocks per 1/20 s tick)

| | Value | From |
|---|---|---|
| Start | 0.1 below the eyes, 0.16 to the left | 1.8.9 (to the right there: the rod hand) |
| Launch | your view direction at 1.5, with a tiny random spread (±0.0075 per axis) | 1.8.9 |
| Each tick | move; then speed -0.04 down; then keep 92% (50% touching ground or a wall) | 1.8.9 |
| Hitting a block | stops where it touches, keeps 10% of its speed | ours (1.8.9 stops dead, then comes loose with a random 0–20%) |
| Catching players | their box counts 0.3 bigger on every side | 1.8.9 |
| Knockback | an unsprinted sword hit's: from the victim's server copy, halve its motion, 0.4 away from the thrower's feet, 0.4 up | 1.8 servers |
| Hangs at | 0.6 of the player's height (chest) | ours (1.8.9: 0.8, the neck) |
| Line breaks at | 32 blocks | 1.8.9 |

A level throw lands about 10.6 blocks away; thrown 20 degrees up, about 15.6.

## How it's built

- `scripts/Combat/Bobber.cs`: the flight, worked out once per Minecraft tick from the cast
  alone, so every game computes the same path. Between ticks it moves in a straight line, and
  that line is what's checked for players, the way Minecraft checks projectiles.
- `scripts/Gameplay/Game.Rod.cs`: throwing, catching and showing bobbers. The netcode follows
  the sword's (see [netcode.md](netcode.md)): the thrower's game decides whom its bobber
  catches, from where players are drawn on its screen, and the victim's game applies the hit.
- `scripts/Gameplay/BobberView.cs`: the bobber and its sagging line, drawn after the players
  move so the line stays on the rod tip.
- Practice bots that fight back throw their rod at enemies 3.5 to 8 blocks away, about every
  1.5 seconds. Chase bots don't.

## Verified

`tests/RodTests.cs`: the cast and flight match a port of 1.8.9's bobber (`McBobber` in
`tests/McReference.cs`) at every tick, a level throw lands where 1.8.9's does, bobbers never
pass through walls and slide down them, which player it catches (enlarged box, nearest first,
edges, over the head), hooking and dropping, and how rod hits count for hit protection.
`tools/smoke_test.sh` requires every bot player, including the laggy one, to land rod hits.

## Open questions

- Balance: with no cooldown, a player can rod someone every half second to keep them away.
  1.8 players had to switch to the rod and back. Watch for this in playtests.
