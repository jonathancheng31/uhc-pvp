# Fishing rod

Our own rod, designed by the owner (2026-10-21) and inspired by 1.8 PvP rods. Its flight and
knockback numbers start from 1.8.9's (`EntityFishHook`, plus the 1.8 server software Hypixel
runs, which is where rod knockback on players comes from). Numbers live in
`scripts/Core/Tuning.cs` under "Fishing rod".

## How it plays

- **Right-click throws the rod**: it's always in your other hand, with no slot switching. The
  sword stays in your main hand.
- **A short cooldown** stops spamming it: until it's over, right-clicks do nothing and the old
  bobber stays out. It's 0.75 s by default (since 0.4.2, at the owner's request), a bit longer
  than the half second of hit protection, so rod after rod can't keep someone pinned away.
  Since 0.4.10 it's a twist, so the host sets it on the Twists screen: from Off (1.8: every
  right-click throws a new bobber) up to 1.5 s, in steps of 0.05 s (one Minecraft tick). If
  the host changes it while yours is running, yours finishes at its old length and the new
  one counts from your next throw. A small bar under the crosshair shows it, emptying as the
  rod gets ready (since 0.4.4; before, a cover on the rod's hotbar slot). A respawn makes the
  rod ready at once.
- **Right-click again** once it's ready and the old bobber vanishes while a new one flies out.
  There's no separate reel-in.
- **A bobber that hits an enemy catches them:** it knocks them back away from you and hangs on
  their chest. No damage, no pull.
- **How hard it knocks back is a twist** (since 0.4.11, the owner's request: "make another
  slider to adjust the fishing rod knockback"). The host sets "Rod knockback" on the Twists
  screen, in the Fishing rod column under the cooldown: from Off to 200%, in 5% steps. 100% is
  1.8's (an unsprinted sword hit's) and the default, so nothing changes until the host moves it.
  It scales the push away from you and the lift together, the lift's cap included, so a higher
  setting sends people both farther and higher; their own motion is halved at any setting, as
  for any hit. Because more lift means more time in the air, the flight grows faster than the
  percentage: a player standing still lands 0.7 blocks away at 50% (0.25 high), 1.8 at 100%
  (1 high), 3.3 at 150% (2.1 high) and 4.9 at 200% (3.6 high). At Off a catch still catches
  them (the bobber hangs on them) and still starts their hit protection, but doesn't move them
  at all, so a sword hit right after does its damage with no knockback. Sword hits never change.
  If the host changes it mid-match, it counts from the next catch.
- **It counts as a hit for hit protection** (the owner's choice, like 1.8):
  - A rod hit starts the victim's half-second protection, with 0 as the "last damage".
  - A sword hit inside that half second is a stronger hit: full damage, but no knockback.
  - A rod hit on someone still protected doesn't catch them; the bobber flies on through.
- Teammates can't be caught (friendly fire is off).
- **Only a flying bobber catches** (since 0.4.10, the owner's choice). Once it lies still it's
  a leftover: walking onto it catches no one. 1.8.9's practically never catches anyone from
  the ground either: it never stays put there, but its path along the floor is next to nothing.
- **The bobber disappears** when you throw again, when you die, when it's more than 32 blocks
  from you, or when it falls out of the world. Since 0.4.10 it also goes on its own, so the
  line doesn't stay stretched across your screen (the owner: "after sitting on ground or player
  for a bit, disappears so the line isn't stuck on screen"): 2 s after it stops moving (lying on
  the ground or on top of a wall), and a quarter of a second after it catches someone, long
  enough to see the catch land. If the player it hangs on dies, it drops, and once it lies
  still its 2 s count. A bobber still flying, or falling down a wall, never disappears on its
  own. The next throw is a normal throw, and the cooldown is unaffected.
- **Nobody can be hit or caught where they're drawn dead,** or at the spot they died while their
  respawn is still on its way to your screen (since 0.4.10; before, a dropped bobber, or a
  sword swing, could catch a bot at its old body the moment it respawned, across the arena).

## Numbers (Minecraft units: blocks and blocks per 1/20 s tick)

| | Value | From |
|---|---|---|
| Start | 0.1 below the eyes, 0.16 to the left | 1.8.9 (to the right there: the rod hand) |
| Launch | your view direction at 1.5, with a tiny random spread (±0.0075 per axis) | 1.8.9 |
| Each tick | move; then speed -0.04 down; then keep 92% (50% touching ground or a wall) | 1.8.9 |
| Hitting a block | stops where it touches, keeps 10% of its speed | ours (1.8.9 stays where it was that tick, short of the surface, then comes loose every tick with a random 0–20%) |
| Catching players | their box counts 0.3 bigger on every side | 1.8.9 |
| Knockback | an unsprinted sword hit's: from the victim's server copy, halve its motion, 0.4 away from the thrower's feet, 0.4 up (at most 0.4) | 1.8 servers |
| Rod knockback | the push away, the lift and its cap times the host's setting: Off (no knockback at all) to 200%, 100% by default; the halving stays | ours (a twist, since 0.4.11) |
| Hangs at | 0.6 of the player's height (chest) | ours (1.8.9: 0.8, the neck) |
| Line breaks at | 32 blocks | 1.8.9 |
| Catches | only while moving | 1.8.9 in effect (its path on the ground is next to nothing) |
| At rest | moving less than 0.01 in a tick | ours |
| Disappears | 2 s after coming to rest; 0.25 s after catching someone | ours (1.8.9's stays until you switch items or reel in, a fraction of a second after a catch in 1.8 PvP) |
| Cooldown | 15 ticks (0.75 s) after each throw by default; the host can set Off to 1.5 s | ours (1.8 has none; switching to the rod and back slowed it) |

A level throw lands about 10.6 blocks away; thrown 20 degrees up, about 15.6.

## How it's built

- `scripts/Combat/Bobber.cs`: the flight, worked out once per Minecraft tick from the cast
  alone, so every game computes the same path. Between ticks it moves in a straight line, and
  that line is what's checked for players, the way Minecraft checks projectiles. The path is
  kept, so any time gives the same answer in any order (since 0.4.10: a host bot's bobber is
  checked for catches ahead of where it's drawn, and was drawn about a block ahead, stepping 20
  times a second). It also says when the bobber came to rest and when it disappears; every game
  works that out from the flight and the catch time it already has, so it vanishes at the same
  moment of the flight everywhere with no message, and the thrower's game stops looking for
  catches once it's at rest.
- `scripts/Combat/RodCooldown.cs`: the cooldown, counted in our 1/60 s ticks on the thrower's
  game (each player's own game decides when they can throw, like their clicks). Its length is
  the match's rule at the moment of the throw.
- `scripts/Gameplay/Game.Rod.cs`: throwing, catching and showing bobbers. The netcode follows
  the sword's (see [netcode.md](netcode.md)): the thrower's game decides whom its bobber
  catches, from where players are drawn on its screen (since 0.4.8, 1.8.9's head start in the
  past; a bot's, from where it sees them, through the same delay), and the victim's game applies
  the hit. Everyone else's bobber, the host's bots' included, is shown on the same delay as they
  are, so it leaves their hand and catches people as drawn.
- `scripts/Combat/CombatRules.cs`: the knockback. `ApplyKnockback` is the victim's game landing
  a hit's knockback, used by the game and by the bot duels in the tests alike; for a rod catch
  it reads the match's rod knockback as the hit lands (since 0.4.11) and at Off sends nothing.
- `scripts/Gameplay/BobberView.cs`: the bobber and its sagging line, drawn after the players
  move so the line stays on the rod tip.
- Practice bots that fight back use the rod at 3.5 to 8 blocks as players do (since 0.4.7): to
  open a fight as you come at them, to extend their combo (catching you in the air as your hit
  protection ends) and to stop your chase when you're comboing them; never into your protection.
  How often depends on their skill (Easy only opens). They follow the match's cooldown. With a
  short one, or none, they wait to see whether their bobber caught you (its flight, then their
  reaction time) before throwing again, instead of making it vanish on the way. Chase bots
  don't rod. See [combat.md](combat.md), "Practice bots".

## Verified

`tests/RodTests.cs`: the cast and flight match a port of 1.8.9's bobber (`McBobber` in
`tests/McReference.cs`) at every tick, a level throw lands where 1.8.9's does, bobbers never
pass through walls and slide down them, which player it catches (enlarged box, nearest first,
edges, over the head), hooking and dropping, how rod hits count for hit protection, and that
spam-clicking throws only once per cooldown. Since 0.4.10: a cooldown of any length lasts
exactly that long with the bar right, Off lets every right-click throw, a change mid-cooldown
applies from the next throw, and with no cooldown bots almost never throw away their own
bobber; when the bobber disappears (at rest on the floor or on a wall top, hooked, dropped,
still moving, falling down a wall) and that it catches no one once at rest; that asking for an
earlier time after a later one gives where it was then, and the moment it vanishes doesn't
depend on the order. Since 0.4.11: rod knockback at 100% is exactly the knockback from before
it was a twist; at other settings the push, the lift and its cap scale while the halving stays;
at Off a catch changes nothing about how the victim moves but still starts their hit
protection; sword hits move the same at every setting; and the flight numbers above, measured
through a whole catch (a thrown bobber, the catch, the knockback landing at the victim's next
Minecraft tick). `tests/CoreTests.cs`: nobody is hit at their old body after a respawn.
`tools/smoke_test.sh` requires every bot player, including the laggy one, to land rod hits,
with a 0.5 s rod cooldown and 150% rod knockback set by the host.

## Open questions

- Balance: whether 0.75 s is the right default cooldown (`Tuning.DefaultRodCooldown`); the
  host can try others on the Twists screen. Before 0.4.2 there was none, and a player could rod someone every half second
  to keep them away.
- Balance: which rod knockback feels right (`Tuning.DefaultRodKnockback`, 1.8's 100% for now).
  Bots rod the same way at every setting, including Off, where a catch can't stop a chase.
