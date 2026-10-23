using System;
using System.Collections.Generic;
using UhcPvp.Autoload;
using UhcPvp.Combat;
using UhcPvp.Core;
using UhcPvp.Movement;

namespace UhcPvp.Gameplay;

/// <summary>How a practice bot behaves.</summary>
public enum BotMode
{
    /// <summary>Stands still and faces the nearest enemy: a training dummy.</summary>
    Still,
    /// <summary>Strafes left and right while facing you: a moving target.</summary>
    Strafe,
    /// <summary>Chases the nearest enemy and fights back like a good player (see <see cref="BotSkill"/>).</summary>
    Fight,
    /// <summary>Moves exactly like <see cref="Fight"/> but never swings or rods: a target that runs at you.</summary>
    Chase,
    /// <summary>Sprints in circles, hops and sneaks (for automated multiplayer tests).</summary>
    Wander,
}

/// <summary>
/// What a bot wants to do this tick: which keys it holds (and where it looks: the input's yaw
/// and <paramref name="Pitch"/>), whether it clicks and whether it throws its rod.
/// </summary>
public readonly record struct BotIntent(MovementInput Input, bool Attack, bool Rod = false, double Pitch = 0);

/// <summary>What a bot knows about itself this tick.</summary>
/// <param name="Feet">Where it stands.</param>
/// <param name="SprintKnockback">Whether its next hit would get sprint knockback (it has w-tapped since its last sprint hit).</param>
/// <param name="CanClick">Whether a click now would swing (no wasted-click lockout).</param>
/// <param name="SinceHurt">Seconds since a full hit on it last landed (infinity if never).</param>
public readonly record struct BotSelf(
    Vec3d Feet, double Yaw, double Pitch, bool OnGround, bool SprintKnockback, bool CanClick, bool RodReady,
    double SinceHurt);

/// <summary>What a bot sees of a player on the other team.</summary>
/// <param name="Feet">Where they're drawn on the bot's game.</param>
/// <param name="SinceHurt">Seconds since a full hit on them last showed (their red flash; infinity if never).</param>
/// <param name="HurtByRod">Whether that hit was a rod's (so a sword hit inside its protection still counts).</param>
public readonly record struct BotFoe(int Id, Vec3d Feet, double Yaw, double SinceHurt, bool HurtByRod);

/// <summary>
/// Practice-bot AI. It "plays" through the same controls a person does: it turns its view,
/// presses movement keys and clicks, and its hits go through exactly the same rules.
///
/// The fighting bot plays like a 1.8 player, with human limits (docs/combat.md, "Practice
/// bots"): it sees you a reaction time late and leads your movement (<see cref="BotPerception"/>),
/// its aim trails and wanders (<see cref="BotAim"/>), it w-taps after sprint hits, waits out your
/// hit protection instead of clicking into it and keeps just out of your reach meanwhile,
/// strafes unpredictably, and uses the rod to open fights, extend its combos and escape yours.
/// When your hit lands it may escape your combo: click you right back (hit select), jump on the
/// exact tick (jump reset), hold W against the knockback, strafe off to one side; or let the
/// knockback carry it. Under the wasted-clicks twist it times its clicks instead of spamming.
/// How often and how well it does each is its <see cref="BotSkill"/>.
///
/// Everything except <see cref="Think(Player, IReadOnlyList{Player}, double)"/> works without a
/// scene, so tests can run whole fights between bots (tests/BotDuel.cs).
/// </summary>
public sealed class BotBrain
{
    private static readonly int N = Tuning.SubticksPerMcTick;
    /// <summary>Hit protection after a full hit (the red flash lasts as long).</summary>
    private static readonly double ProtectionSeconds = (double)Tuning.HurtImmunityThresholdMcTicks / Tuning.McTickRate;
    /// <summary>Simple dummies (stand still, strafe) turn this fast, with a little wobble, in degrees per second.</summary>
    private const double DummyTurnSpeed = 540;
    private const double DummyWobble = 1.5;
    /// <summary>On Minecraft's 1/20 s tick grid, hit protection ends 0.45 to 0.5 s after the hit.</summary>
    private static readonly double EarliestProtectionEnd = ProtectionSeconds - 1.0 / Tuning.McTickRate;
    /// <summary>Two full hits on someone are at least 0.45 s apart; times closer than this are the same hit.</summary>
    private const double SameHitSeconds = 0.1;

    private readonly Random _random;
    private readonly BotAim _aim = new();
    private readonly Dictionary<int, FoeMemory> _foes = new();
    private readonly List<BotFoe> _foeViews = new();
    private double _time;
    private int? _targetId;

    // Movement.
    private double _strafeTimer;
    private int _strafeDirection = 1;
    /// <summary>Strafing out of a combo: which way, and until when.</summary>
    private int _escapeDirection;
    private double _escapeUntil = double.NegativeInfinity;
    /// <summary>Whether it clicks back to cut the knockback of the hit it just took...</summary>
    private bool _hitSelect;
    private int _hitSelectDelay;
    /// <summary>...and whether it holds W against that knockback.</summary>
    private bool _holdForward = true;
    /// <summary>Subticks before a (jump reset) jump starts.</summary>
    private int _jumpDelay;
    /// <summary>Subticks left of a w-tap (W let go) or s-tap (S held).</summary>
    private int _tapTicks;
    private bool _tapBack;
    private int _jumpTicks;
    // Fighting.
    private double _nextClickAt;
    private double _lastLanded = double.NegativeInfinity;
    private double _lastTaken = double.NegativeInfinity;
    private double _lastSelfHurt = double.NegativeInfinity;
    private RodPlan _rod;
    private RodPlan _declined;
    private double _rodPlanUntil;
    private double _rodRethinkAt;
    private double _lastRodAt = double.NegativeInfinity;

    public BotBrain(BotMode mode, int seed, BotDifficulty difficulty = Tuning.DefaultBotDifficulty)
    {
        Mode = mode;
        Difficulty = difficulty;
        _random = new Random(seed);
    }

    public BotMode Mode { get; set; }

    /// <summary>How well it fights (only matters for <see cref="BotMode.Fight"/> and <see cref="BotMode.Chase"/>).</summary>
    public BotDifficulty Difficulty
    {
        get => _difficulty;
        set
        {
            _difficulty = value;
            Skill = BotSkill.For(value);
        }
    }
    private BotDifficulty _difficulty;

    /// <summary>The numbers behind <see cref="Difficulty"/> (tests can set their own).</summary>
    public BotSkill Skill { get; set; } = Tuning.NormalBot;

    /// <summary>
    /// The bot clicked at a player (or at nothing: <paramref name="targetId"/> null).
    /// <paramref name="result"/> is what its hit did (null when nothing was hit);
    /// <paramref name="sprintKnockback"/> whether it carried sprint knockback.
    /// </summary>
    public void OnAttack(int? targetId, HitResult? result, bool sprintKnockback)
    {
        if (targetId is not int id || result is not HitResult hit || hit == HitResult.Ignored)
        {
            return;
        }
        FoeMemory memory = MemoryOf(id);
        if (hit == HitResult.Hurt)
        {
            _lastLanded = _time;
            StartProtection(memory, _time, byRod: false);
        }
        else
        {
            memory.StrongerUsed = true;
        }
        if (sprintKnockback && _random.NextDouble() < Skill.WTapChance)
        {
            // A w-tap (let go of W for a moment) or, sometimes, an s-tap (tap S), so the next hit
            // gets sprint knockback again and the target flies out of reach.
            _tapTicks = N * _random.Next(1, 3);
            _tapBack = _random.NextDouble() < 0.3;
        }
    }

    /// <summary>A tick for a bot in the match: reads the scene, thinks, and turns the bot's view.</summary>
    public BotIntent Think(Player self, IReadOnlyList<Player> enemies, double delta)
    {
        double now = Network.Instance.Now;
        _foeViews.Clear();
        foreach (Player enemy in enemies)
        {
            _foeViews.Add(new BotFoe(enemy.PeerId, enemy.ShownFeet, enemy.Yaw, now - enemy.HurtShownAt, enemy.HurtShownByRod));
        }
        PlayerMotor? motor = self.Motor;
        var view = new BotSelf(
            self.Feet, self.Yaw, self.Pitch, motor?.OnGround ?? true, motor?.ServerSprinting ?? false, self.CanAttack,
            self.RodCooldown.Ready, now - self.HurtShownAt);
        BotIntent intent = Think(view, _foeViews, Session.Instance.Rules, delta);
        self.Look((float)intent.Input.Yaw, (float)intent.Pitch);
        return intent with { Input = intent.Input with { Yaw = self.Yaw }, Pitch = self.Pitch };
    }

    /// <summary>
    /// A tick: what the bot does, given what it knows about itself, the enemies it sees and the
    /// match's twists. Doesn't need a scene.
    /// </summary>
    public BotIntent Think(in BotSelf self, IReadOnlyList<BotFoe> foes, MatchRules rules, double delta)
    {
        _time += delta;
        foreach (BotFoe foe in foes)
        {
            MemoryOf(foe.Id).Seen.Observe(_time, foe.Feet);
        }
        return Mode switch
        {
            BotMode.Still => Still(self, foes, delta),
            BotMode.Strafe => Strafe(self, foes, delta),
            BotMode.Fight => Fight(self, foes, rules, delta, clicks: true),
            BotMode.Chase => Fight(self, foes, rules, delta, clicks: false),
            _ => Wander(self, delta),
        };
    }

    // --- Simple modes --------------------------------------------------------------------

    private BotIntent Still(in BotSelf self, IReadOnlyList<BotFoe> foes, double delta)
    {
        (double yaw, double pitch) = FaceSimply(self, Nearest(self, foes), delta);
        return new BotIntent(new MovementInput(0, 0, false, false, false, yaw), false, Pitch: pitch);
    }

    private BotIntent Strafe(in BotSelf self, IReadOnlyList<BotFoe> foes, double delta)
    {
        (double yaw, double pitch) = FaceSimply(self, Nearest(self, foes), delta);
        _strafeTimer -= delta;
        if (_strafeTimer <= 0)
        {
            _strafeDirection = -_strafeDirection;
            _strafeTimer = 0.8 + _random.NextDouble() * 0.6;
        }
        return new BotIntent(new MovementInput(0, _strafeDirection, false, false, false, yaw), false, Pitch: pitch);
    }

    private BotIntent Wander(in BotSelf self, double delta)
    {
        double cycle = _time % 6.0;
        bool sneak = cycle is > 4.5 and < 5.5;
        bool jump = !sneak && _time % 1.5 < 0.1;
        double strafe = cycle is > 2 and < 3 ? 1 : 0;
        return new BotIntent(new MovementInput(1, strafe, jump, !sneak, sneak, self.Yaw + 60 * delta), false, Pitch: 0);
    }

    /// <summary>Dummies turn toward the target's chest at a steady speed, with a little wobble.</summary>
    private (double Yaw, double Pitch) FaceSimply(in BotSelf self, BotFoe? target, double delta)
    {
        if (target is not BotFoe foe)
        {
            return (self.Yaw, self.Pitch);
        }
        (double yaw, double pitch) = LookAngles(Eye(self.Feet), foe.Feet + new Vec3d(0, Tuning.BotAimHeight, 0));
        double maxTurn = DummyTurnSpeed * delta;
        double wobble = (_random.NextDouble() - 0.5) * 2 * DummyWobble * Math.Min(delta * 20, 1);
        double dYaw = Math.Clamp(BotAim.WrapDegrees(yaw - self.Yaw), -maxTurn, maxTurn) + wobble;
        double dPitch = Math.Clamp(pitch - self.Pitch, -maxTurn, maxTurn);
        return (self.Yaw + dYaw, self.Pitch + dPitch);
    }

    // --- Fighting --------------------------------------------------------------------------

    private BotIntent Fight(in BotSelf self, IReadOnlyList<BotFoe> foes, MatchRules rules, double delta, bool clicks)
    {
        BotSkill skill = Skill;
        NoticeOwnHurt(self);
        if (PickTarget(self, foes) is not BotFoe foe)
        {
            _targetId = null;
            return new BotIntent(new MovementInput(0, 0, false, true, false, self.Yaw), false, Pitch: self.Pitch);
        }
        FoeMemory memory = MemoryOf(foe.Id);
        NoticeFoeHurt(foe, memory);

        // What it sees: the target a reaction time ago, led by how they were moving then.
        Vec3d seen = memory.Seen.Perceived(_time, skill.ReactionSeconds, skill.Anticipation, Tuning.BotMotionWindow);
        Vec3d seenVelocity = memory.Seen.VelocityAt(_time - skill.ReactionSeconds, Tuning.BotMotionWindow);
        Vec3d eye = Eye(self.Feet);
        Vec3d toFoe = seen - self.Feet;
        double distance = toFoe.HorizontalLength;
        Vec3d away = distance > 1e-6 ? new Vec3d(toFoe.X / distance, 0, toFoe.Z / distance) : Vec3d.Forward(self.Yaw);
        // Positive when they're getting farther away, negative when they come at it.
        double receding = seenVelocity.X * away.X + seenVelocity.Z * away.Z;

        bool comboing = _lastLanded > _lastTaken && _time - _lastLanded < Tuning.BotComboMemory;
        bool comboed = _lastTaken > _lastLanded && _time - _lastTaken < Tuning.BotComboMemory;
        // Hit protection as far as it can tell, in seconds left: theirs (from its own hits and their
        // red flash) and its own (they can't hurt it until it's over).
        double theirs = memory.HurtAt + ProtectionSeconds - _time;
        double mine = ProtectionSeconds - self.SinceHurt;
        // Patience: it holds its clicks until it reckons their protection is over...
        bool timedClicks = rules.WastedClickDelay > 0;
        bool waiting = memory.Patient && _time < (timedClicks ? memory.ClickFrom : memory.SpamFrom);
        // ...where a sloppier bot clicks into a rod's protection for the stronger hit's damage.
        bool strongerOpen = !memory.Patient && memory.ByRod && !memory.StrongerUsed && theirs > 0;

        // --- Aim ---
        Vec3d aimAt = seen + new Vec3d(0, Tuning.BotAimHeight, 0);
        if (_rod is RodPlan.Open or RodPlan.Extend or RodPlan.Escape)
        {
            // About to throw: lead the target by the bobber's flight.
            aimAt += seenVelocity.WithY(0) * RodFlightSeconds(distance);
        }
        (double wantYaw, double wantPitch) = LookAngles(eye, aimAt);
        (double yaw, double pitch) = _aim.Step(self.Yaw, self.Pitch, wantYaw, wantPitch, skill, delta, _random);
        Vec3d look = HitScan.LookVector(yaw, pitch);
        Box box = HitScan.Hitbox(seen);
        bool onTarget = box.Contains(eye) || box.RayEntry(eye, look, Tuning.BlockReach) is not null;
        double reachTo = box.DistanceTo(eye);

        // --- Movement ---
        double forward = 1;
        if (_tapTicks > 0)
        {
            _tapTicks--;
            forward = _tapBack ? -1 : 0;
        }
        else if (waiting && mine <= 0.05 && theirs > 0.1 && distance < Tuning.BotWaitDistance)
        {
            // Waiting out their protection while they can hit it: keep just outside their reach.
            forward = distance < Tuning.BotWaitDistance - 0.6 ? -1 : 0;
        }
        else if (distance < 0.8)
        {
            forward = 0; // don't run through them
        }
        else if (!self.SprintKnockback && theirs > 0.15 && _time - _lastLanded > 0.15
            && _random.NextDouble() < skill.WTapChance * delta * 10)
        {
            // Its next hit wouldn't knock back fully (it skipped the w-tap): reset sprint now.
            _tapTicks = N;
            _tapBack = false;
            forward = 0;
        }
        double strafe = StrafeKey(seenVelocity, distance, comboing, rules, delta, yaw);
        bool jump = false;
        if (self.SinceHurt < Tuning.BotKnockbackFlight && !_holdForward)
        {
            forward = 0; // lets their knockback carry it
        }
        if (_jumpDelay > 0)
        {
            _jumpDelay--;
        }
        else if (_jumpTicks > 0)
        {
            // A jump reset: a sprint-jump toward them (W held) as the knockback arrives.
            _jumpTicks--;
            jump = true;
            forward = 1;
        }
        else if (distance > Tuning.BotSprintJumpDistance && forward > 0 && receding > -2)
        {
            // Sprint-jump to close a gap, like players do (not into someone charging at it).
            jump = true;
        }

        // --- Clicks ---
        bool attack = false;
        if (self.SinceHurt < Tuning.BotKnockbackWindow)
        {
            // Flying with their knockback: it only clicks to hit select, right away while they're
            // still in reach, which cuts the knockback (a click while sprinting slows it to 60%).
            // Otherwise it leaves the mouse alone and takes the knockback in full.
            if (_hitSelect && _hitSelectDelay > 0)
            {
                _hitSelectDelay--;
            }
            else if (clicks && _hitSelect && self.CanClick && reachTo <= Tuning.Reach + Tuning.BotHitSelectSlack)
            {
                attack = true;
                _hitSelect = false;
                _nextClickAt = _time + ClickGap(skill);
            }
        }
        else if (clicks && self.CanClick && _time >= _nextClickAt)
        {
            if (timedClicks)
            {
                // Every useless click locks clicking for a moment, so it only clicks when it should
                // count: on target, in reach, and (as far as it can tell) out of their protection.
                bool counts = _time >= memory.ClickFrom || strongerOpen;
                // (On target: its crosshair inside where it sees them, within reach; a patient bot
                // waits until it's well inside.)
                double margin = Tuning.BotTimedClickMargin * skill.Patience;
                attack = counts && box.Grow(-margin).RayEntry(eye, look, Tuning.Reach) is not null;
            }
            else
            {
                // Plain 1.8: clicking costs nothing, so it clicks away while its crosshair is on
                // them and they're close (unless it's patiently waiting out their protection).
                // How close is its skill: a bot that clicks only once they're in reach lands its
                // first click the moment they come in, where one that starts further out
                // (spamming) gets there at a random moment of its clicking rhythm.
                attack = !waiting && box.RayEntry(eye, look, Tuning.Reach + skill.ReachSlack) is not null;
            }
            if (attack)
            {
                _nextClickAt = _time + ClickGap(skill);
            }
        }

        // --- Rod ---
        bool rod = clicks && self.RodReady
            && Rod(eye, yaw, pitch, seen, seenVelocity, distance, receding, theirs, mine, comboing, comboed);

        return new BotIntent(new MovementInput(forward, strafe, jump, true, false, yaw), attack, rod, pitch);
    }

    /// <summary>
    /// Which way to strafe: unpredictably in a fight (how long each way is random, shorter for
    /// better bots and when strafing is snappier or faster), and mostly straight while closing
    /// in from afar or chasing a combo, where strafing would only slow it down.
    /// </summary>
    private double StrafeKey(Vec3d seenVelocity, double distance, bool comboing, MatchRules rules, double delta, double yaw)
    {
        BotSkill skill = Skill;
        _strafeTimer -= delta;
        if (_time < _escapeUntil)
        {
            return _escapeDirection; // strafing out of their combo
        }
        if (_strafeTimer > 0)
        {
            return _strafeDirection;
        }
        // Snappier or faster strafing (twists) makes changing direction more worthwhile.
        double quicker = 1 + rules.StrafeGrip + (rules.StrafeSpeed - 1) * 2;
        double length = (skill.StrafeMinSeconds + _random.NextDouble() * (skill.StrafeMaxSeconds - skill.StrafeMinSeconds)) / quicker;
        _strafeTimer = length;
        if (distance > 6 || comboing)
        {
            // Closing in or chasing: mostly straight, sometimes a short weave.
            _strafeDirection = _random.NextDouble() < 0.6 ? 0 : (_random.NextDouble() < 0.5 ? -1 : 1);
            _strafeTimer = length * 0.6;
            return _strafeDirection;
        }
        double r = _random.NextDouble();
        if (r < 0.1)
        {
            _strafeDirection = 0;
            _strafeTimer = length * 0.5;
            return 0;
        }
        // Against someone strafing, often circle the other way (both aims must swing faster),
        // otherwise switch sides.
        Vec3d left = Vec3d.Left(yaw);
        double theirSideways = seenVelocity.X * left.X + seenVelocity.Z * left.Z;
        int circle = theirSideways > 1 ? -1 : theirSideways < -1 ? 1 : 0;
        _strafeDirection = circle != 0 && r < 0.45 ? circle
            : _strafeDirection == 0 ? (_random.NextDouble() < 0.5 ? -1 : 1)
            : -_strafeDirection;
        return _strafeDirection;
    }

    /// <summary>
    /// Whether to throw the rod now. It rods you as you come at it (stopping your charge, so it
    /// can hit you as your protection ends), as you fly out of sword reach in its combo (catching
    /// you in the air the moment your protection ends, to keep you there), and as you chase it in
    /// yours (to stop the chase). Each chance is taken or passed on once; then it throws as soon
    /// as its aim would catch you.
    /// </summary>
    private bool Rod(
        Vec3d eye, double yaw, double pitch, Vec3d seen, Vec3d seenVelocity, double distance, double receding,
        double theirs, double mine, bool comboing, bool comboed)
    {
        BotSkill skill = Skill;
        // A bobber only catches someone whose protection is over by the time it gets there.
        double theirsThen = theirs - RodFlightSeconds(distance);
        RodPlan chance = RodPlan.None;
        if (theirsThen <= 0 && distance >= Tuning.BotRodMinDistance && distance <= Tuning.BotRodMaxDistance)
        {
            if (comboing && skill.RodTactics && receding > 0 && theirsThen > -Tuning.BotRodExtendWindow)
            {
                chance = RodPlan.Extend;
            }
            else if (comboed && skill.RodTactics && receding < -1 && mine < 0.2)
            {
                chance = RodPlan.Escape;
            }
            else if (!comboing && !comboed && receding < 1 && _time - _lastRodAt > Tuning.BotRodOpenSeconds)
            {
                chance = RodPlan.Open;
            }
        }
        if (chance == RodPlan.None)
        {
            _rod = RodPlan.None;
            return false;
        }
        if (_rod == RodPlan.Declined && _declined == chance && _time < _rodRethinkAt)
        {
            return false;
        }
        if (_rod != chance)
        {
            // A new chance: take it, or let it go for a while.
            if (_random.NextDouble() >= skill.RodChance)
            {
                Decline(chance);
                return false;
            }
            _rod = chance;
            _rodPlanUntil = _time + Tuning.BotRodAimSeconds;
        }
        if (_time >= _rodPlanUntil)
        {
            Decline(chance); // couldn't line it up in time
            return false;
        }
        if (!WouldCatch(eye, yaw, pitch, seen, seenVelocity))
        {
            return false;
        }
        _rod = RodPlan.None;
        _lastRodAt = _time;
        return true;
    }

    private void Decline(RodPlan chance)
    {
        _rod = RodPlan.Declined;
        _declined = chance;
        _rodRethinkAt = _time + Tuning.BotRodThinkSeconds * (1 + _random.NextDouble());
    }

    /// <summary>
    /// Whether a bobber thrown now along the bot's view would catch the target where it believes
    /// they'll be (flying the bobber's path without blocks; it never needs more than 10 ticks).
    /// </summary>
    private static bool WouldCatch(Vec3d eye, double yaw, double pitch, Vec3d seen, Vec3d seenVelocity)
    {
        Vec3d position = Bobber.CastOrigin(eye, yaw);
        Vec3d motion = Bobber.CastVelocity(yaw, pitch, Vec3d.Zero);
        var target = new RodTarget[1];
        double tickSeconds = 1.0 / Tuning.McTickRate;
        for (int tick = 0; tick < 10; tick++)
        {
            Vec3d next = position + motion;
            target[0] = new RodTarget(0, seen + seenVelocity.WithY(0) * ((tick + 1) * tickSeconds));
            if (Bobber.FirstHit(position, next, target) is not null)
            {
                return true;
            }
            position = next;
            motion = new Vec3d(motion.X, motion.Y - Tuning.BobberGravity, motion.Z) * Tuning.BobberDrag;
        }
        return false;
    }

    /// <summary>About how long a bobber takes to fly <paramref name="distance"/> blocks.</summary>
    private static double RodFlightSeconds(double distance)
    {
        // Speed 1.5 per tick, keeping 92% each tick: covers 1.5 (1 - 0.92^k) / 0.08 in k ticks.
        double f = 1 - distance * (1 - Tuning.BobberDrag) / Tuning.RodCastSpeed;
        double ticks = f > 0.05 ? Math.Log(f) / Math.Log(Tuning.BobberDrag) : 30;
        return ticks / Tuning.McTickRate;
    }

    /// <summary>
    /// A full hit on the bot just landed: its aim is thrown off, and it decides how to escape
    /// the combo this time (each a chance from its skill): click right back to cut the
    /// knockback (hit select), jump to take less of it (jump reset), strafe off to one side.
    /// </summary>
    private void NoticeOwnHurt(in BotSelf self)
    {
        // (Real and game clocks differ by a little, so the same hit can't be told apart by less
        // than this; two full hits are always 0.45 s apart.)
        double hurtAt = _time - self.SinceHurt;
        if (!(hurtAt > _lastSelfHurt + SameHitSeconds))
        {
            return;
        }
        BotSkill skill = Skill;
        _lastSelfHurt = hurtAt;
        _lastTaken = _time;
        _aim.Jolt(skill.HitJoltDegrees, _random);
        _hitSelect = _random.NextDouble() < skill.HitSelectChance;
        // It only cuts the knockback while they're still in reach, so the click must come on the
        // first tick of the knockback; a person often gets it a Minecraft tick or two late.
        _hitSelectDelay = LateMcTicks(skill) * N;
        _holdForward = _random.NextDouble() < skill.HoldForwardChance;
        if (self.OnGround && _random.NextDouble() < skill.JumpResetChance)
        {
            // Only a jump on the very Minecraft tick the knockback arrives counts; a person often
            // gets it a tick or two late (which does nothing: it's in the air by then).
            _jumpDelay = LateMcTicks(skill) * N;
            _jumpTicks = N;
        }
        if (_random.NextDouble() < skill.EscapeChance)
        {
            _escapeDirection = _random.NextDouble() < 0.5 ? -1 : 1;
            _escapeUntil = _time + Tuning.BotEscapeSeconds;
        }
    }

    /// <summary>
    /// Their red flash, once the bot can have noticed it (a reaction time after it starts): a hit
    /// by someone else, or the bot's own rod. Its own sword hits it knows about as it clicks.
    /// </summary>
    private void NoticeFoeHurt(BotFoe foe, FoeMemory memory)
    {
        if (foe.SinceHurt < Skill.ReactionSeconds || !double.IsFinite(foe.SinceHurt))
        {
            return;
        }
        double hurtAt = _time - foe.SinceHurt;
        if (hurtAt > memory.HurtAt + SameHitSeconds)
        {
            StartProtection(memory, hurtAt, foe.HurtByRod);
        }
    }

    /// <summary>A full hit on a foe at <paramref name="at"/>: their protection runs until half a second later.</summary>
    private void StartProtection(FoeMemory memory, double at, bool byRod)
    {
        BotSkill skill = Skill;
        memory.HurtAt = at;
        memory.ByRod = byRod;
        memory.StrongerUsed = false;
        memory.Patient = _random.NextDouble() < skill.Patience;
        // When it reckons the protection ends: a little after half a second, give or take its timing.
        double error = BotAim.Gaussian(_random) * skill.TimingErrorSeconds;
        memory.ClickFrom = at + ProtectionSeconds + Math.Clamp(0.01 + error, -0.1, 0.2);
        // Where clicking costs nothing, it starts again a little early instead, so its first hit
        // that can count comes as soon as possible (protection can end 0.45 s after the hit).
        memory.SpamFrom = at + EarliestProtectionEnd - Math.Abs(error);
    }

    /// <summary>
    /// How many Minecraft ticks late a move that must land on one exact tick comes: 0 most of the
    /// time for a precise bot (68% at a timing error of 0.025 s), more often 1 or 2 for a sloppy one.
    /// </summary>
    private int LateMcTicks(BotSkill skill) =>
        (int)Math.Round(Math.Abs(BotAim.Gaussian(_random)) * skill.TimingErrorSeconds * Tuning.McTickRate);

    /// <summary>The time until the next click: about 1 / clicks per second, irregular like a person's clicking.</summary>
    private double ClickGap(BotSkill skill)
    {
        double sigma = skill.ClickSpread;
        double gap = Math.Exp(BotAim.Gaussian(_random) * sigma - sigma * sigma / 2) / skill.ClicksPerSecond;
        return Math.Max(gap, 1.0 / 25);
    }

    /// <summary>The enemy to fight: the nearest, but it sticks with its current one unless another is clearly closer.</summary>
    private BotFoe? PickTarget(in BotSelf self, IReadOnlyList<BotFoe> foes)
    {
        BotFoe? current = null;
        BotFoe? nearest = null;
        double nearestDistance = double.MaxValue;
        double currentDistance = double.MaxValue;
        foreach (BotFoe foe in foes)
        {
            double d = (foe.Feet - self.Feet).HorizontalLength;
            if (foe.Id == _targetId)
            {
                current = foe;
                currentDistance = d;
            }
            if (d < nearestDistance)
            {
                nearest = foe;
                nearestDistance = d;
            }
        }
        BotFoe? pick = current is not null && currentDistance < nearestDistance + 2 ? current : nearest;
        _targetId = pick?.Id;
        return pick;
    }

    private static BotFoe? Nearest(in BotSelf self, IReadOnlyList<BotFoe> foes)
    {
        BotFoe? best = null;
        double bestDistance = double.MaxValue;
        foreach (BotFoe foe in foes)
        {
            double d = (foe.Feet - self.Feet).HorizontalLength;
            if (d < bestDistance)
            {
                best = foe;
                bestDistance = d;
            }
        }
        return best;
    }

    private FoeMemory MemoryOf(int id)
    {
        if (!_foes.TryGetValue(id, out FoeMemory? memory))
        {
            memory = new FoeMemory();
            _foes[id] = memory;
        }
        return memory;
    }

    private static Vec3d Eye(Vec3d feet) => feet + new Vec3d(0, Tuning.EyeHeight, 0);

    /// <summary>Yaw and pitch (degrees) that look from <paramref name="eye"/> at <paramref name="point"/>.</summary>
    public static (double Yaw, double Pitch) LookAngles(Vec3d eye, Vec3d point)
    {
        Vec3d d = point - eye;
        double yaw = Math.Atan2(-d.X, -d.Z) * 180.0 / Math.PI;
        double pitch = Math.Atan2(d.Y, Math.Sqrt(d.X * d.X + d.Z * d.Z)) * 180.0 / Math.PI;
        return (yaw, pitch);
    }

    /// <summary>What a bot has seen of one enemy and what it believes about their hit protection.</summary>
    private sealed class FoeMemory
    {
        public BotPerception Seen { get; } = new();
        /// <summary>When the bot believes the last full hit on them landed (its own clock).</summary>
        public double HurtAt { get; set; } = double.NegativeInfinity;
        /// <summary>Whether that was a rod hit (no damage), so a sword hit inside it still counts once.</summary>
        public bool ByRod { get; set; }
        public bool StrongerUsed { get; set; }
        /// <summary>Whether the bot waits out this protection (hit selecting) rather than clicking into it.</summary>
        public bool Patient { get; set; }
        /// <summary>When the bot reckons its hits count again...</summary>
        public double ClickFrom { get; set; } = double.NegativeInfinity;
        /// <summary>...and when it starts clicking again if clicks cost nothing (no wasted-clicks twist).</summary>
        public double SpamFrom { get; set; } = double.NegativeInfinity;
    }

    private enum RodPlan
    {
        None,
        Declined,
        Open,
        Extend,
        Escape,
    }
}
