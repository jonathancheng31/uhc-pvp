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
    /// <summary>A hit is thrown out if the attacker's view of the victim was older than this (seconds).</summary>
    public const double LagCap = 0.150;
    /// <summary>Seconds between clock-sync pings.</summary>
    public const double PingInterval = 0.5;

    // --- Player body ----------------------------------------------------------------------

    public const double PlayerWidth = 0.6;
    public const double PlayerHeight = 1.8;
    public const double EyeHeight = 1.62;
    /// <summary>How much lower the camera sits while sneaking.</summary>
    public const double SneakEyeDrop = 0.08;
    public const double MaxHealth = 20.0;

    // --- Camera and mouse -----------------------------------------------------------------

    /// <summary>Vertical field of view in degrees, same as Minecraft's "Normal".</summary>
    public const float Fov = 70f;
    /// <summary>Minecraft's sensitivity slider value. 0.5 is shown as "100%" in Minecraft.</summary>
    public const double DefaultMouseSensitivity = 0.5;
    /// <summary>FOV multiplier while sprinting: Minecraft uses ((sprint speed / walk speed) + 1) / 2.</summary>
    public const double SprintFovMultiplier = (SprintSpeedMultiplier + 1.0) / 2.0;
    /// <summary>Fraction of the remaining FOV change applied per MC tick.</summary>
    public const double FovSmoothingPerMcTick = 0.5;

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
    /// <summary>Seconds after taking damage during which further hits do nothing (10 MC ticks).</summary>
    public const double HurtImmunity = 0.5;
    public const double CritMultiplier = 1.5;
    public const double KnockbackHorizontal = 0.4;
    public const double KnockbackVertical = 0.4;
    /// <summary>Extra knockback from a sprinting hit; this is what makes w-tapping work.</summary>
    public const double SprintKnockbackBonus = 0.5;
    public const double SprintHitVerticalBonus = 0.1;

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
