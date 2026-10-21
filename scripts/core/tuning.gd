class_name Tuning
extends RefCounted
## Every gameplay number lives here so the feel can be tuned in one place.
##
## Starting values come from Minecraft 1.8.9. Minecraft simulates 20 ticks per
## second; we simulate 60. Constants ending in _PER_MC_TICK keep Minecraft's
## units (blocks per 1/20 s) and are converted by the code that uses them.

# --- Simulation and networking ----------------------------------------------

## Game ticks per second. Minecraft uses 20; more ticks means less delay.
const SIM_RATE := 60
const MC_TICK_RATE := 20
const DEFAULT_PORT := 7777
## Up to 6v6.
const MAX_PLAYERS := 12
const MAX_PLAYERS_PER_TEAM := 6
## Own-state updates per second each player sends to the host.
const STATE_SEND_RATE := 60
## Snapshots of everyone per second the host sends to each player.
const SNAPSHOT_RATE := 60
## Never show other players less than this far in the past (seconds).
const INTERP_MIN_DELAY := 0.020
## A hit is thrown out if the attacker's view of the victim was older than this (seconds).
const LAG_CAP := 0.150
## Seconds between clock-sync pings.
const PING_INTERVAL := 0.5

# --- Player body -------------------------------------------------------------

const PLAYER_WIDTH := 0.6
const PLAYER_HEIGHT := 1.8
const EYE_HEIGHT := 1.62
const MAX_HEALTH := 20.0

# --- Camera and mouse ---------------------------------------------------------

## Vertical field of view in degrees, same as Minecraft's "Normal".
const FOV := 70.0
## Minecraft's sensitivity slider value. 0.5 is shown as "100%" in Minecraft.
const DEFAULT_MOUSE_SENSITIVITY := 0.5


## Degrees the camera turns per mouse count, using Minecraft's sensitivity curve,
## so aim muscle memory carries over.
static func degrees_per_mouse_count(sensitivity: float) -> float:
	var f := sensitivity * 0.6 + 0.2
	return f * f * f * 8.0 * 0.15


# --- Movement (Minecraft 1.8.9; used from the movement milestone on) ----------

const WALK_SPEED_ATTRIBUTE := 0.1
const SPRINT_MULTIPLIER := 1.3
const SNEAK_INPUT_MULTIPLIER := 0.3
const GROUND_SLIPPERINESS := 0.6
const AIR_FRICTION := 0.91
const AIR_ACCELERATION_PER_MC_TICK := 0.02
const GRAVITY_PER_MC_TICK := 0.08
const VERTICAL_DRAG_PER_MC_TICK := 0.98
const JUMP_VELOCITY_PER_MC_TICK := 0.42
const SPRINT_JUMP_BOOST_PER_MC_TICK := 0.2

# --- Combat (Minecraft 1.8.9) -------------------------------------------------

## Max distance from the attacker's eyes to the victim's hitbox, in blocks.
const REACH := 3.0
## Seconds after taking damage during which further hits do nothing (10 MC ticks).
const HURT_IMMUNITY := 0.5
const CRIT_MULTIPLIER := 1.5
const KNOCKBACK_HORIZONTAL_PER_MC_TICK := 0.4
const KNOCKBACK_VERTICAL_PER_MC_TICK := 0.4
## Extra knockback from a sprinting hit; this is what makes w-tapping work.
const SPRINT_KNOCKBACK_BONUS_PER_MC_TICK := 0.5
const SPRINT_HIT_VERTICAL_BONUS_PER_MC_TICK := 0.1

# --- Arena --------------------------------------------------------------------

## Inside width and depth of the arena, in blocks.
const ARENA_SIZE := 48
const ARENA_WALL_HEIGHT := 6
## Y of the grass surface players stand on.
const ARENA_FLOOR_Y := 5
## Distance from the arena center to each team's spawn line.
const SPAWN_DISTANCE := 12.0
