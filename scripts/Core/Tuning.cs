namespace UhcPvp.Core;

/// <summary>
/// Every gameplay number lives here so the feel can be tuned in one place.
/// Starting values come from Minecraft 1.8.9. Minecraft simulates 20 ticks per second and
/// we simulate 60; values documented as "per MC tick" keep Minecraft's units (blocks per
/// 1/20 s) and are converted by the code that uses them (see Movement/MotionModel.cs).
/// </summary>
public static class Tuning
{
    // --- Simulation and networking ------------------------------------------------------

    /// <summary>Game ticks per second. Minecraft uses 20; more ticks means less delay.</summary>
    public const int SimRate = 60;
    public const int McTickRate = 20;
    /// <summary>Our ticks per Minecraft tick.</summary>
    public const int SubticksPerMcTick = SimRate / McTickRate;
    public const int DefaultPort = 7777;
    /// <summary>Up to 6v6.</summary>
    public const int MaxPlayers = 12;
    public const int MaxPlayersPerTeam = 6;
    /// <summary>Own-state updates per second each player sends to the host.</summary>
    public const int StateSendRate = 60;
    /// <summary>Never show other players less than this far in the past (seconds).</summary>
    public const double InterpMinDelay = 0.020;
    /// <summary>
    /// A hit is thrown out if the attacker's view of the victim was older than this (seconds), or
    /// than the head start if that's more: views up to the head start are the rule, older ones are
    /// ghost hits (docs/netcode.md).
    /// </summary>
    public const double LagCap = 0.150;
    /// <summary>
    /// Head start (1.8.9's display delay, a match rule): other players are drawn, and hit, this many
    /// seconds in the past, the same at any ping. 1.8.9 shows them 175 ms in the past on average
    /// even at 0 ping (where a click finds a sprinting player, by a port of its entity tracker and
    /// client glide: 100 to 250 ms depending on tick timing), which gives whoever is chasing a head
    /// start after every hit. 0 is how we drew others before 0.4.8: as fresh as possible.
    /// </summary>
    public const double McHeadStart = 0.175;
    /// <summary>
    /// The head start a new match starts with: shorter than 1.8.9's (<see cref="McHeadStart"/>),
    /// the owner's pick from playing 0.4.8 (2026-10-23).
    /// </summary>
    public const double DefaultHeadStart = 0.075;
    public const double MaxHeadStart = 0.250;
    public const double HeadStartStep = 0.025;
    /// <summary>Seconds between clock-sync pings.</summary>
    public const double PingInterval = 0.5;

    // --- Player body ----------------------------------------------------------------------

    public const double PlayerWidth = 0.6;
    public const double PlayerHeight = 1.8;
    public const double EyeHeight = 1.62;
    /// <summary>How much lower the camera sits while sneaking.</summary>
    public const double SneakEyeDrop = 0.08;
    public const double MaxHealth = 20.0;

    // --- Players pushing each other (a twist; 1.8.9 has none, see Movement/Pushing.cs) --------

    /// <summary>
    /// Walking into someone, your bodies end up overlapping by this much (blocks) and no more, at
    /// 100%. A little squish is what lets you shove someone who isn't pushing back.
    /// </summary>
    public const double PushSquish = 0.2;
    /// <summary>
    /// Walking into someone, each Minecraft tick you may close this share of what's left to the
    /// squish depth, so you ease to a stop instead of thudding. Each game reacts to the others as
    /// it sees them, late by the lag, and with everyone easing in by half, a crowd pressing in
    /// under lag sits right on the edge of bobbing in and out; a third leaves a safe margin.
    /// </summary>
    public const double PushEaseIn = 1.0 / 3.0;
    /// <summary>
    /// Someone not walking into you slides out of an overlap: each Minecraft tick the two of you
    /// would part by this fraction of it (half each), at 100%. This sets how fast a shoved player
    /// slides away (about 1.1 blocks/s when sprinted into).
    /// </summary>
    public const double PushYield = 1.0;
    /// <summary>
    /// Walking into someone but deeper than the squish (lag, or a soft setting): you ease back by
    /// this fraction of the extra each Minecraft tick (half each), at 100%. Gentler than
    /// <see cref="PushYield"/>, which keeps crowds steady under lag.
    /// </summary>
    public const double PushBackOff = 0.7;
    /// <summary>...but nobody is moved out faster than this together (blocks per Minecraft tick, so 5 blocks/s), at 100%: a slide, never a launch.</summary>
    public const double PushMaxSeparation = 0.25;
    /// <summary>
    /// Below 100%, bodies are softer: you can keep pushing through someone at up to this speed
    /// (blocks per Minecraft tick) at 50%, faster below that and slower above:
    /// speed x (1 / setting - 1). From 60% up nobody gets through.
    /// </summary>
    public const double PushThroughSpeed = 0.1;
    /// <summary>
    /// You may follow someone backing away from you by this share of their speed: enough to keep
    /// up while shoving them, without the two of you reacting to each other back and forth.
    /// </summary>
    public const double PushFollow = 0.5;
    /// <summary>
    /// Your keys count as walking into someone when they point within 60 degrees of them (the
    /// cosine); in between walking into them and past them, the two rules blend.
    /// </summary>
    public const double PushPressCos = 0.5;
    /// <summary>
    /// Passes when working out one push against several players at once (squeezed in a crowd):
    /// each pass shares the corrections out, so conflicting ones meet in the middle.
    /// </summary>
    public const int PushPasses = 4;
    /// <summary>Players further apart than this (blocks between bodies) can't touch this tick.</summary>
    public const double PushRange = 1.5;
    /// <summary>Two players remember which side of each other they came from while closer than this (blocks between bodies).</summary>
    public const double PushMemoryGap = 0.3;
    /// <summary>Players closer than this (centre to centre) are "on top of each other" and part in a fixed direction.</summary>
    public const double PushMinDistance = 0.01;
    /// <summary>The "Player pushing" twist's setting: 0 is off (1.8.9), 1 is solid; in steps of 10%.</summary>
    public const double McPushing = 0.0;
    /// <summary>Off in a new match, as in 1.8.9 (the owner's pick from playing 0.4.8).</summary>
    public const double DefaultPushing = McPushing;
    public const double MaxPushing = 1.0;
    public const double PushingStep = 0.1;

    // --- Camera and mouse -----------------------------------------------------------------

    /// <summary>
    /// Vertical field of view in degrees by default, Minecraft's "Normal". Players can change it
    /// in Settings within Minecraft's range (<see cref="MinFov"/> to <see cref="MaxFov"/>, "Quake
    /// Pro"). Unlike 1.8.9, sprinting never widens it (the owner's choice, 0.4.2).
    /// </summary>
    public const int DefaultFov = 70;
    public const int MinFov = 30;
    public const int MaxFov = 110;
    /// <summary>
    /// The sword and rod in first person are always drawn as if at this field of view, whatever
    /// the player's setting, like Minecraft's hand.
    /// </summary>
    public const float HandFov = 70f;
    /// <summary>Minecraft's sensitivity slider value. 0.5 is shown as "100%" in Minecraft.</summary>
    public const double DefaultMouseSensitivity = 0.5;

    /// <summary>
    /// Degrees the camera turns per mouse count, using Minecraft's sensitivity curve, so
    /// aim muscle memory carries over.
    /// </summary>
    public static double DegreesPerMouseCount(double sensitivity)
    {
        double f = sensitivity * 0.6 + 0.2;
        return f * f * f * 8.0 * 0.15;
    }

    // --- Movement (Minecraft 1.8.9, per MC tick) -------------------------------------------

    /// <summary>Base movement speed attribute.</summary>
    public const double WalkSpeed = 0.1;
    public const double SprintSpeedMultiplier = 1.3;
    /// <summary>Movement keys are scaled by this while sneaking.</summary>
    public const double SneakInputMultiplier = 0.3;
    /// <summary>Minecraft scales movement input by 0.98 every tick.</summary>
    public const double InputScale = 0.98;
    public const double BlockSlipperiness = 0.6;
    /// <summary>Horizontal velocity kept per tick in the air.</summary>
    public const double AirFriction = 0.91;
    /// <summary>Minecraft's ground acceleration constant (0.6 * 0.91)^3.</summary>
    public const double GroundAccelerationConstant = 0.16277136;
    public const double AirAcceleration = 0.02;
    /// <summary>Sprinting adds this fraction to air acceleration (0.02 becomes 0.026).</summary>
    public const double SprintAirAccelerationBonus = 0.3;
    public const double Gravity = 0.08;
    /// <summary>Vertical velocity kept per tick.</summary>
    public const double VerticalDrag = 0.98;
    public const double JumpVelocity = 0.42;
    /// <summary>Forward boost from jumping while sprinting.</summary>
    public const double SprintJumpBoost = 0.2;

    // --- Our twists (not 1.8.9; the host sets them for the whole match, see MatchRules) ----
    // Each has 1.8.9's value (Mc..., together MatchRules.Minecraft, plain 1.8.9) and the value a
    // new match starts with (Default..., together MatchRules.Default): the owner's picks from
    // playing 0.4.8 (2026-10-23), the snappiest strafing and a shorter head start than 1.8.9's.

    /// <summary>
    /// Faster strafing: on the ground, the sideways part of your movement is this many times
    /// 1.8.9's, but never more than a straight sprint in total (docs/movement.md). 1.0 is 1.8.9.
    /// The host picks a value between 1 and <see cref="MaxStrafeSpeed"/> in steps of
    /// <see cref="StrafeSpeedStep"/>.
    /// </summary>
    public const double McStrafeSpeed = 1.0;
    /// <summary>1.8.9's in a new match.</summary>
    public const double DefaultStrafeSpeed = McStrafeSpeed;
    public const double MaxStrafeSpeed = 1.5;
    public const double StrafeSpeedStep = 0.05;
    /// <summary>
    /// Snappier strafing: on the ground, at each Minecraft tick, your sideways speed covers this
    /// fraction of the gap to where your keys are taking it, so starting, stopping and switching
    /// between A and D get quicker (docs/movement.md). 0 is 1.8.9; 1 is instant.
    /// </summary>
    public const double McStrafeGrip = 0.0;
    /// <summary>Instant in a new match (switching A to D in one Minecraft tick, not 1.8.9's quarter second).</summary>
    public const double DefaultStrafeGrip = 1.0;
    public const double MaxStrafeGrip = 1.0;
    public const double StrafeGripStep = 0.05;
    /// <summary>
    /// Wasted clicks: a click that does nothing (a miss, or a player still in hit protection)
    /// blocks clicking for this many seconds (docs/combat.md). 0 is 1.8.9.
    /// </summary>
    public const double McWastedClickDelay = 0.0;
    /// <summary>Off in a new match, as in 1.8.9.</summary>
    public const double DefaultWastedClickDelay = McWastedClickDelay;
    public const double MaxWastedClickDelay = 0.3;
    public const double WastedClickDelayStep = 0.05;
    /// <summary>
    /// Combo knockback: a sprint hit's extra sideways push (<see cref="SprintKnockbackBonus"/>) is
    /// this fraction of 1.8.9's, so each sprint hit carries the victim less far and the attacker
    /// catches up sooner (docs/combat.md). The base push, the lift, plain hits and the rod are
    /// untouched. 1 is 1.8.9; the host can go down to <see cref="MinComboKnockback"/> (below about
    /// 60% strafing stops working as a way out of a combo).
    /// </summary>
    public const double McComboKnockback = 1.0;
    /// <summary>1.8.9's in a new match.</summary>
    public const double DefaultComboKnockback = McComboKnockback;
    public const double MinComboKnockback = 0.6;
    public const double ComboKnockbackStep = 0.05;
    /// <summary>Minimum MC ticks between jumps while holding jump.</summary>
    public const int JumpCooldownMcTicks = 10;
    /// <summary>Velocities smaller than this are zeroed each tick.</summary>
    public const double MinMotion = 0.005;
    /// <summary>Forward input needed to start or keep sprinting.</summary>
    public const double SprintForwardThreshold = 0.8;
    /// <summary>MC ticks within which a second W press starts sprinting.</summary>
    public const int DoubleTapSprintWindowMcTicks = 7;
    /// <summary>Step size Minecraft uses when stopping a sneaking player at a ledge.</summary>
    public const double SneakEdgeStep = 0.05;
    /// <summary>How far below the feet sneaking looks for ground before stopping at a ledge.</summary>
    public const double SneakEdgeDepth = 1.0;

    // --- Combat (Minecraft 1.8.9) ----------------------------------------------------------

    /// <summary>Max distance from the attacker's eyes to the victim's hitbox, in blocks.</summary>
    public const double Reach = 3.0;
    /// <summary>How far the crosshair looks for blocks. Hits can't go through blocks closer than the target.</summary>
    public const double BlockReach = 4.5;
    /// <summary>Hitboxes are this much bigger on every side for aiming (Minecraft's collision border).</summary>
    public const double HitboxBorder = 0.1;
    /// <summary>Damage of the kit's sword: a diamond sword (player base 1 + sword 7).</summary>
    public const double SwordDamage = 8.0;
    /// <summary>
    /// Armor points of the kit (out of 20): diamond helmet and chestplate, iron leggings and boots
    /// (3 + 8 + 5 + 2). Damage is multiplied by (25 - armor) / 25, so a sword hit does 2.24.
    /// </summary>
    public const int KitArmorPoints = 18;
    public const double CritMultiplier = 1.5;
    /// <summary>MC ticks of hit protection after a hit (hurtResistantTime).</summary>
    public const int HurtResistantMcTicks = 20;
    /// <summary>
    /// For this many MC ticks after a full hit (the first half of the protection), hits only
    /// count if they're stronger than the last one, and give no base knockback. Counted on
    /// Minecraft's 1/20 s tick grid, like a server does (see HitProtection).
    /// </summary>
    public const int HurtImmunityThresholdMcTicks = HurtResistantMcTicks / 2;
    /// <summary>MC ticks the red hurt flash and camera tilt last.</summary>
    public const int HurtFlashMcTicks = 10;
    /// <summary>Minecraft's hurt camera tilt at its peak, degrees. Players scale it in Settings.</summary>
    public const float HurtTiltDegrees = 14f;
    public const double KnockbackHorizontal = 0.4;
    public const double KnockbackVertical = 0.4;
    /// <summary>Extra knockback from a sprinting hit; this is what makes w-tapping work.</summary>
    public const double SprintKnockbackBonus = 0.5;
    public const double SprintHitVerticalBonus = 0.1;
    /// <summary>A sprint hit slows the attacker to this fraction of their speed and stops their sprint.</summary>
    public const double SprintHitSlowdown = 0.6;
    /// <summary>MC ticks an arm swing takes.</summary>
    public const int SwingMcTicks = 6;
    /// <summary>
    /// Minecraft 1.8's miss penalty (leftClickCounter): clicking at nothing (no player within
    /// reach and no block within block reach) blocks clicks for this many MC ticks, but only
    /// while the button stays held: it clears at the end of any tick the button is up.
    /// </summary>
    public const int MissClickCooldownMcTicks = 10;
    /// <summary>
    /// A client reports its position to the server only after moving more than this many blocks
    /// since its last report (EntityPlayerSP.onUpdateWalkingPlayer)...
    /// </summary>
    public const double PositionReportDistance = 0.03;
    /// <summary>...or after this many MC ticks without a report.</summary>
    public const int PositionReportMaxTicks = 20;
    /// <summary>Knockback travels in a velocity packet: each axis clamped to this many blocks per tick...</summary>
    public const double VelocityPacketClamp = 3.9;
    /// <summary>...and rounded toward zero to a multiple of 1/8000.</summary>
    public const double VelocityPacketScale = 8000.0;
    /// <summary>Hits between teammates do nothing.</summary>
    public const bool FriendlyFire = false;
    /// <summary>Seconds before a dead player respawns.</summary>
    public const double RespawnDelay = 3.0;

    // --- Fishing rod (our own rod; numbers start from 1.8.9's EntityFishHook, per MC tick) ---

    /// <summary>A rod hit's damage: none, it only knocks back (and counts for hit protection).</summary>
    public const double RodDamage = 0.0;
    /// <summary>Bobber launch speed, blocks per MC tick.</summary>
    public const double RodCastSpeed = 1.5;
    /// <summary>Random spread of the launch direction (each axis of the unit direction gets a gaussian times this).</summary>
    public const double RodCastSpread = 0.0075;
    /// <summary>The bobber starts this far below the eyes...</summary>
    public const double RodCastDrop = 0.1;
    /// <summary>...and this far to the side of them (Minecraft starts it at the rod hand).</summary>
    public const double RodCastSideOffset = 0.16;
    /// <summary>Size of the bobber's (cube) collision box.</summary>
    public const double BobberSize = 0.25;
    /// <summary>Speed lost to gravity each MC tick (before drag).</summary>
    public const double BobberGravity = 0.04;
    /// <summary>Fraction of its motion the bobber keeps each MC tick in the air...</summary>
    public const double BobberDrag = 0.92;
    /// <summary>...and while touching the ground or a wall.</summary>
    public const double BobberGroundDrag = 0.5;
    /// <summary>
    /// Flying into a block stops the bobber where it touches, keeping this fraction of its
    /// speed (1.8.9's stops dead, then comes loose with a random 0 to 20%).
    /// </summary>
    public const double BobberImpactSpeedKept = 0.1;
    /// <summary>Players count this much bigger on every side for the bobber.</summary>
    public const double BobberHitboxGrow = 0.3;
    /// <summary>A caught bobber hangs at this fraction of the player's height: the chest (1.8.9's hangs at the neck, 0.8).</summary>
    public const double BobberHookHeight = 0.6;
    /// <summary>The line breaks (the bobber vanishes) when it's farther than this from the thrower.</summary>
    public const double RodLineLength = 32.0;
    /// <summary>
    /// The rod's cooldown (a twist, see MatchRules): after a throw, the rod can't be thrown again
    /// for this many seconds. 1.8.9 has none: 0, where every right-click throws.
    /// </summary>
    public const double McRodCooldown = 0.0;
    /// <summary>
    /// 0.75 s (15 MC ticks) in a new match: the owner's choice in 0.4.2, so it can't be spammed.
    /// Longer than the half second of hit protection, so back-to-back rods can't keep someone
    /// pinned away.
    /// </summary>
    public const double DefaultRodCooldown = 0.75;
    public const double MaxRodCooldown = 1.5;
    /// <summary>One MC tick, so every length is a whole number of our ticks.</summary>
    public const double RodCooldownStep = 0.05;

    // --- Practice bots ----------------------------------------------------------------------

    /// <summary>Seconds before a practice bot comes back after dying.</summary>
    public const double BotRespawnDelay = 2.0;
    /// <summary>A practice bot heals to full after this many seconds without being hit.</summary>
    public const double BotHealDelay = 5.0;
    /// <summary>How far in front of you the practice dummy appears.</summary>
    public const double PracticeDummyDistance = 5.0;
    /// <summary>
    /// The skill new practice bots start with, until the host picks another in the pause menu
    /// (it's remembered for the next match).
    /// </summary>
    public const BotDifficulty DefaultBotDifficulty = BotDifficulty.Normal;
    /// <summary>
    /// How the fighting bot plays at each skill (see <see cref="BotSkill"/> for what each number
    /// means, and docs/combat.md for how they were picked and measured). Easy: slow to react,
    /// loose aim, about 7 clicks a second into hit protection, rarely w-taps, rods only to open,
    /// and lets your knockback carry it (never hit selects, jump resets or strafes out), so you
    /// can practise combos on it.
    /// </summary>
    public static readonly BotSkill EasyBot = new(
        ReactionSeconds: 0.2, Anticipation: 0.5, AimErrorDegrees: 4, AimSettleSeconds: 0.12, TurnSpeed: 400,
        HitJoltDegrees: 10, ClicksPerSecond: 7, ClickSpread: 0.3, ReachSlack: 0.9, Patience: 0,
        TimingErrorSeconds: 0.08, WTapChance: 0.4, StrafeMinSeconds: 0.7, StrafeMaxSeconds: 1.6, RodChance: 0.3,
        RodTactics: false, HitSelectChance: 0, HoldForwardChance: 0.1, JumpResetChance: 0, EscapeChance: 0);
    /// <summary>
    /// Normal: a decent player. About 11 clicks a second, usually w-taps and waits out your hit
    /// protection, escapes about half your hits one way or another, rods to open, extend and escape.
    /// </summary>
    public static readonly BotSkill NormalBot = new(
        ReactionSeconds: 0.1, Anticipation: 0.85, AimErrorDegrees: 2.2, AimSettleSeconds: 0.08, TurnSpeed: 600,
        HitJoltDegrees: 6, ClicksPerSecond: 11, ClickSpread: 0.25, ReachSlack: 0, Patience: 0.6,
        TimingErrorSeconds: 0.04, WTapChance: 0.85, StrafeMinSeconds: 0.35, StrafeMaxSeconds: 1.0, RodChance: 0.6,
        RodTactics: true, HitSelectChance: 0.6, HoldForwardChance: 0.6, JumpResetChance: 0.3, EscapeChance: 0.3);
    /// <summary>
    /// Hard: a strong UHC player. Quick, steady aim, about 12.5 clicks a second, always w-taps,
    /// times its hits, and escapes combos the way good players do, with well-timed hit selects
    /// and jump resets.
    /// </summary>
    public static readonly BotSkill HardBot = new(
        ReactionSeconds: 0.08, Anticipation: 0.9, AimErrorDegrees: 1.5, AimSettleSeconds: 0.06, TurnSpeed: 800,
        HitJoltDegrees: 4, ClicksPerSecond: 12.5, ClickSpread: 0.2, ReachSlack: 0, Patience: 0.9,
        TimingErrorSeconds: 0.025, WTapChance: 1.0, StrafeMinSeconds: 0.2, StrafeMaxSeconds: 0.8, RodChance: 0.6,
        RodTactics: true, HitSelectChance: 0.9, HoldForwardChance: 0.9, JumpResetChance: 0.6, EscapeChance: 0.5);
    /// <summary>
    /// For this long after a sword hit lands on it (seconds), a bot is flying with the knockback.
    /// It only clicks then to hit select (<see cref="BotSkill.HitSelectChance"/>).
    /// </summary>
    public const double BotKnockbackWindow = 0.25;
    /// <summary>
    /// A hit select is a click as your hit lands, anticipated rather than aimed: the bot makes it
    /// if it sees you within this much beyond reach (blocks); whether it connects is up to the rules.
    /// </summary>
    public const double BotHitSelectSlack = 0.5;
    /// <summary>
    /// A bot counts itself carried by a sword hit's knockback for this long (seconds: the
    /// attacker's wait until their next hit can land), whether it holds W against it or not
    /// (<see cref="BotSkill.HoldForwardChance"/>).
    /// </summary>
    public const double BotKnockbackFlight = 0.5;
    /// <summary>
    /// Under the wasted-clicks twist a patient bot (<see cref="BotSkill.Patience"/> 1) only clicks
    /// when its crosshair is at least this far inside your hitbox as it sees you (blocks), so it
    /// rarely clicks at air; less patient bots click with less to spare.
    /// </summary>
    public const double BotTimedClickMargin = 0.15;
    /// <summary>A bot that strafes out of a combo keeps at it this long after the hit (seconds).</summary>
    public const double BotEscapeSeconds = 0.7;
    /// <summary>A bot's aim wanders off and back about this slowly, in seconds (how long a wander lasts).</summary>
    public const double BotAimWanderSeconds = 0.35;
    /// <summary>Bots aim at this height above a player's feet: the chest.</summary>
    public const double BotAimHeight = 1.2;
    /// <summary>A bot judges how someone is moving from where they were over this long, in seconds.</summary>
    public const double BotMotionWindow = 0.1;
    /// <summary>A bot counts itself in a combo (giving or taking) for this long after the last hit, in seconds.</summary>
    public const double BotComboMemory = 1.0;
    /// <summary>A fighting bot throws its rod at enemies between these distances (blocks).</summary>
    public const double BotRodMinDistance = 3.5;
    public const double BotRodMaxDistance = 8.0;
    /// <summary>A bot opens with its rod at most this often (seconds), so it fights with its sword in between.</summary>
    public const double BotRodOpenSeconds = 1.5;
    /// <summary>
    /// To extend a combo, a bot rods you so the bobber arrives no later than this after your hit
    /// protection ends (seconds), catching you while you're still in the air.
    /// </summary>
    public const double BotRodExtendWindow = 0.3;
    /// <summary>After passing on a chance to rod, a bot thinks again after about this long (seconds; up to twice it).</summary>
    public const double BotRodThinkSeconds = 0.3;
    /// <summary>Having decided to rod, a bot gives up if it can't line the throw up within this long (seconds).</summary>
    public const double BotRodAimSeconds = 0.6;
    /// <summary>A fighting bot sprint-jumps to close a gap wider than this (blocks).</summary>
    public const double BotSprintJumpDistance = 7.0;
    /// <summary>
    /// A disciplined bot waiting for your hit protection to end keeps at least this far away
    /// (blocks, feet to feet), just outside your reach, so you can't hit it first.
    /// </summary>
    public const double BotWaitDistance = 3.4;

    // --- Arena -----------------------------------------------------------------------------

    /// <summary>Inside width and depth of the arena, in blocks.</summary>
    public const int ArenaSize = 48;
    public const int ArenaWallHeight = 6;
    /// <summary>Y of the grass surface players stand on.</summary>
    public const int ArenaFloorY = 5;
    /// <summary>Distance from the arena center to each team's spawn line.</summary>
    public const double SpawnDistance = 12.0;
    /// <summary>Falling below this Y sends a player back to spawn.</summary>
    public const double VoidY = -32.0;
}
