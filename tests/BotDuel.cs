using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Godot;
using UhcPvp.Combat;
using UhcPvp.Core;
using UhcPvp.Gameplay;
using UhcPvp.Movement;
using UhcPvp.World;

namespace UhcPvp.Tests;

/// <summary>A fighting brain the duel can run: today's <see cref="BotBrain"/> or the old one.</summary>
public interface IDuelBrain
{
    string Name { get; }
    /// <summary>Whether its clicks come before its move each tick (the game's order for bots since 0.4.7).</summary>
    bool ClicksBeforeMoving => true;
    BotIntent Think(in BotSelf self, IReadOnlyList<BotFoe> foes, MatchRules rules, double delta);
    void OnAttack(int? targetId, HitResult? result, bool sprintKnockback);
}

/// <summary>Today's fighting bot at one skill, for duels.</summary>
public sealed class SkilledBrain : IDuelBrain
{
    private readonly BotBrain _brain;

    public SkilledBrain(BotDifficulty difficulty, int seed)
    {
        _brain = new BotBrain(BotMode.Fight, seed, difficulty);
        Name = BotSkill.Label(difficulty);
    }

    /// <summary>A bot with made-up numbers, for trying changes.</summary>
    public SkilledBrain(string name, BotSkill skill, int seed)
    {
        _brain = new BotBrain(BotMode.Fight, seed) { Skill = skill };
        Name = name;
    }

    public string Name { get; }

    public BotIntent Think(in BotSelf self, IReadOnlyList<BotFoe> foes, MatchRules rules, double delta) =>
        _brain.Think(self, foes, rules, delta);

    public void OnAttack(int? targetId, HitResult? result, bool sprintKnockback) =>
        _brain.OnAttack(targetId, result, sprintKnockback);
}

/// <summary>How one side did in a duel.</summary>
public sealed class DuelSide
{
    public int Clicks { get; set; }
    /// <summary>Clicks that did nothing: at nothing in reach (<see cref="Misses"/>), or into hit protection.</summary>
    public int WastedClicks { get; set; }
    public int Misses { get; set; }
    /// <summary>Full sword hits (damage and knockback).</summary>
    public int Hits { get; set; }
    /// <summary>Sword hits inside protection that still counted (crits, or after a rod).</summary>
    public int StrongerHits { get; set; }
    public int RodThrows { get; set; }
    /// <summary>Throws that made its own bobber vanish while that one was still on its way to the other side.</summary>
    public int RodThrowsLost { get; set; }
    public int RodHits { get; set; }
    public int Crits { get; set; }
    /// <summary>Full sword hits in a row without taking one back.</summary>
    public int BestCombo { get; set; }
    public int Combo { get; set; }
    /// <summary>The length of every combo it landed (full sword hits in a row), as each ended.</summary>
    public List<int> ComboLengths { get; } = new();

    /// <summary>The other side hit back (or the duel ended): the combo so far is over.</summary>
    public void EndCombo()
    {
        if (Combo > 0)
        {
            ComboLengths.Add(Combo);
        }
        Combo = 0;
    }
    public double Damage { get; set; }
    /// <summary>Ticks the other player was within reach, and of those, ticks the crosshair was on them.</summary>
    public int TicksInReach { get; set; }
    public int TicksOnTarget { get; set; }
    /// <summary>Full hits right after its own last full hit (keeping a combo going)...</summary>
    public int Continues { get; set; }
    /// <summary>...and right after one by the other side (turning the fight around).</summary>
    public int Reversals { get; set; }
    /// <summary>
    /// Sword knockbacks it took, and how far they sent it in half a second: those it cut by
    /// clicking the attacker while flying (hit selecting) and those it took in full.
    /// </summary>
    public int KnockbacksCut { get; set; }
    public double CutKnockbackDistance { get; set; }
    public int KnockbacksFull { get; set; }
    public double FullKnockbackDistance { get; set; }
}

public sealed record DuelResult(int Winner, double Seconds, DuelSide A, DuelSide B);

/// <summary>
/// Two bot brains fighting each other without a scene: a copy of the game's flow for players
/// the host runs (Player._PhysicsProcess, Game.TryAttack and ResolveAttack, Game.Rod), built on
/// the same rule classes (movement, aiming, hit protection, knockback, the rod, wasted clicks).
/// A duel starts with both at full health 10 to 14 blocks apart in the arena and ends at the
/// first death (or after a time limit, a draw). Used to measure the bots against each other.
/// </summary>
public sealed class BotDuel
{
    private static readonly int N = Tuning.SubticksPerMcTick;
    private const double Delta = 1.0 / Tuning.SimRate;
    /// <summary>A knockback is measured over this long (the time until the next hit can land).</summary>
    public const double KnockbackSeconds = 0.5;

    private readonly VoxelWorld _world;
    private readonly MatchRules _rules;
    private readonly Random _random;
    private readonly Fighter[] _fighters;
    private readonly List<AimTarget> _aimTargets = new();
    private readonly List<RodTarget> _rodTargets = new();
    private readonly List<BotFoe> _foes = new();
    private double _time;
    /// <summary>Who landed the last full sword hit (0: nobody yet).</summary>
    private int _lastHitter;

    private BotDuel(IDuelBrain a, IDuelBrain b, MatchRules rules, int seed)
    {
        _world = Arena.Build();
        _rules = rules;
        _random = new Random(seed);
        // Somewhere near the middle, 10 to 14 blocks apart, facing each other (roughly).
        Vector3 center = Arena.Center;
        double angle = _random.NextDouble() * 360;
        double apart = 10 + _random.NextDouble() * 4;
        Vec3d mid = new Vec3d(center.X, Tuning.ArenaFloorY, center.Z)
            + new Vec3d(_random.NextDouble() * 6 - 3, 0, _random.NextDouble() * 6 - 3);
        Vec3d dir = Vec3d.Forward(angle);
        var fa = new Fighter(1, a, new PlayerMotor(_world, mid - dir * (apart / 2)), angle + Jitter());
        var fb = new Fighter(2, b, new PlayerMotor(_world, mid + dir * (apart / 2)), angle + 180 + Jitter());
        // Who the game steps first within a tick is fixed per match; mix it up.
        _fighters = _random.NextDouble() < 0.5 ? new[] { fa, fb } : new[] { fb, fa };
        // Start at a random point of Minecraft's 1/20 s tick grid.
        _time = 1000 + _random.NextDouble();
    }

    /// <summary>Fights one duel between <paramref name="a"/> and <paramref name="b"/>.</summary>
    public static DuelResult Fight(
        IDuelBrain a, IDuelBrain b, MatchRules rules, int seed, double maxSeconds = 60, Action<string>? trace = null)
    {
        var duel = new BotDuel(a, b, rules, seed) { Trace = trace };
        return duel.Run(maxSeconds);
    }

    /// <summary>Prints a line per tick about side A, for working on the bot.</summary>
    private Action<string>? Trace { get; init; }

    private double Jitter() => _random.NextDouble() * 20 - 10;

    private DuelResult Run(double maxSeconds)
    {
        Fighter a = _fighters[0].Id == 1 ? _fighters[0] : _fighters[1];
        Fighter b = a == _fighters[0] ? _fighters[1] : _fighters[0];
        int ticks = (int)(maxSeconds * Tuning.SimRate);
        for (int tick = 1; tick <= ticks; tick++)
        {
            _time += Delta;
            StepRods();
            foreach (Fighter f in _fighters)
            {
                Step(f, f == a ? b : a);
            }
            foreach (Fighter f in _fighters)
            {
                // As Game records the states of the players it runs, to show them in the past.
                f.Trail.Observe(_time, f.Motor.Position);
            }
            if (a.Vitality.IsDead || b.Vitality.IsDead)
            {
                int winner = a.Vitality.IsDead ? 2 : 1;
                return Finish(winner, tick * Delta, a, b);
            }
        }
        return Finish(0, maxSeconds, a, b);
    }

    private static DuelResult Finish(int winner, double seconds, Fighter a, Fighter b)
    {
        a.Stats.EndCombo();
        b.Stats.EndCombo();
        return new DuelResult(winner, seconds, a.Stats, b.Stats);
    }

    private void Step(Fighter f, Fighter other)
    {
        if (f.KnockbackTracked && _time - f.HurtAt >= KnockbackSeconds)
        {
            // How far a sword hit sent it, over the knockback's first half second.
            f.KnockbackTracked = false;
            double flown = (f.Motor.Position - f.KnockbackFrom).HorizontalLength;
            if (f.KnockbackCut)
            {
                f.Stats.KnockbacksCut++;
                f.Stats.CutKnockbackDistance += flown;
            }
            else
            {
                f.Stats.KnockbacksFull++;
                f.Stats.FullKnockbackDistance += flown;
            }
        }
        f.Vitality.Tick();
        f.Rod.Tick();
        f.Lockout.Tick();
        f.Subtick = (f.Subtick + 1) % N;
        if (f.Subtick == 0)
        {
            f.Miss.Tick(buttonHeld: false);
        }
        var self = new BotSelf(
            f.Motor.Position, f.Yaw, f.Pitch, f.Motor.OnGround, f.Motor.ServerSprinting, f.CanAttack, f.Rod.Ready,
            _time - f.HurtAt);
        _foes.Clear();
        Vec3d seen = SeenFeet(other);
        _foes.Add(new BotFoe(other.Id, seen, other.Yaw, _time - other.HurtAt, other.HurtByRod));
        BotIntent intent = f.Brain.Think(self, _foes, _rules, Delta);
        if (Trace is not null && f.Id == 1)
        {
            Vec3d eye = f.Eye;
            (double wy, double wp) = BotBrain.LookAngles(eye, other.Motor.Position + new Vec3d(0, 1.2, 0));
            Trace(string.Create(CultureInfo.InvariantCulture,
                $"t={_time - 1000:0.000} d={(other.Motor.Position - f.Motor.Position).HorizontalLength:0.00} "
                + $"yawErr={BotAim.WrapDegrees(intent.Input.Yaw - wy):0.0} pitchErr={intent.Pitch - wp:0.0} "
                + $"fwd={intent.Input.Forward} str={intent.Input.Strafe} jmp={intent.Input.Jump} atk={intent.Attack} rod={intent.Rod} "
                + $"hp={f.Vitality.Health:0.0}/{other.Vitality.Health:0.0} sk={f.Motor.ServerSprinting}"));
        }
        // As Player.Look: angles are floats, yaw wrapped, pitch clamped.
        f.Yaw = Mathf.Wrap((float)intent.Input.Yaw, -180f, 180f);
        f.Pitch = Mathf.Clamp((float)intent.Pitch, -90f, 90f);
        // Aim: how often the crosshair is on the other player while they're in reach.
        AimResult onTarget = HitScan.Aim(
            f.Eye, HitScan.LookVector(f.Yaw, f.Pitch), new[] { new AimTarget(other.Id, seen) }, _world);
        if (HitScan.Hitbox(seen).DistanceTo(f.Eye) <= Tuning.Reach)
        {
            f.Stats.TicksInReach++;
            f.Stats.TicksOnTarget += onTarget.Kind == AimKind.Player ? 1 : 0;
        }
        f.Motor.StrafeSpeed = _rules.StrafeSpeed;
        f.Motor.StrafeGrip = _rules.StrafeGrip;
        // (Pushing stays off: the duels measure fighting, and it's a twist.)
        if (intent.Attack && f.Brain.ClicksBeforeMoving)
        {
            TryAttack(f, other); // as Player._PhysicsProcess does since 0.4.7
        }
        f.Motor.Step(intent.Input with { Yaw = f.Yaw });
        if (intent.Attack && !f.Brain.ClicksBeforeMoving)
        {
            TryAttack(f, other); // as it did up to 0.4.6
        }
        if (intent.Rod)
        {
            ThrowRod(f);
        }
    }

    /// <summary>As Game.TryAttack.</summary>
    private void TryAttack(Fighter attacker, Fighter target)
    {
        if (!attacker.CanAttack)
        {
            return;
        }
        attacker.Stats.Clicks++;
        _aimTargets.Clear();
        _aimTargets.Add(new AimTarget(target.Id, SeenFeet(target)));
        AimResult aim = HitScan.Aim(attacker.Eye, HitScan.LookVector(attacker.Yaw, attacker.Pitch), _aimTargets, _world);
        if (aim.Kind != AimKind.Player)
        {
            if (aim.Kind == AimKind.Miss)
            {
                attacker.Miss.Missed();
            }
            attacker.Stats.Misses++;
            Waste(attacker);
            return;
        }
        bool critical = attacker.Motor.CanCrit;
        bool sprintKnockback = attacker.Motor.ServerSprinting;
        if (attacker.Motor.Sprinting && _time - attacker.HurtAt < KnockbackSeconds)
        {
            // Clicking someone while sprinting slows you to 60%: during your own knockback, that cuts it.
            attacker.KnockbackCut = true;
        }
        HitResult result = Resolve(attacker, target, rod: false, critical, sprintKnockback);
        if (ClickLockout.IsWasted(result))
        {
            Waste(attacker);
        }
        attacker.Motor.OnHitLanded(result != HitResult.Ignored);
        attacker.Brain.OnAttack(target.Id, result, sprintKnockback);
    }

    private void Waste(Fighter attacker)
    {
        attacker.Stats.WastedClicks++;
        attacker.Lockout.Wasted(_rules.WastedClickDelay);
    }

    /// <summary>As Game.ResolveAttack, for a hit made now.</summary>
    private HitResult Resolve(Fighter attacker, Fighter victim, bool rod, bool critical, bool sprintKnockback)
    {
        double raw = rod ? Tuning.RodDamage : CombatRules.RawDamage(Tuning.SwordDamage, critical);
        double before = victim.Vitality.Health;
        HitResult result = victim.Vitality.TakeHit(raw, Tuning.KitArmorPoints, HitProtection.TickAt(_time));
        if (result == HitResult.Ignored)
        {
            return result;
        }
        bool sprint = !rod && sprintKnockback;
        // As the game: from where the two really are, built from the server's copy as it lands,
        // under the duel's rules (combo knockback, and the rod knockback for a rod catch).
        CombatRules.ApplyKnockback(
            victim.Motor, result, attacker.Motor.Position, victim.Motor.Position, attacker.Yaw, rod, sprint, _rules, _random);
        if (result == HitResult.Hurt)
        {
            victim.HurtAt = _time;
            victim.HurtByRod = rod;
            if (!rod)
            {
                victim.KnockbackFrom = victim.Motor.Position;
                victim.KnockbackCut = false;
                victim.KnockbackTracked = true;
            }
        }
        Trace?.Invoke(string.Create(CultureInfo.InvariantCulture,
            $"  HIT by {attacker.Id}: {(rod ? "rod" : result.ToString())} sprint={sprint} "
            + $"d={(victim.Motor.Position - attacker.Motor.Position).HorizontalLength:0.00}"));
        DuelSide stats = attacker.Stats;
        stats.Damage += before - victim.Vitality.Health;
        if (rod)
        {
            stats.RodHits++;
        }
        else if (result == HitResult.Hurt)
        {
            if (_lastHitter == attacker.Id)
            {
                stats.Continues++;
            }
            else if (_lastHitter != 0)
            {
                stats.Reversals++;
            }
            _lastHitter = attacker.Id;
            stats.Hits++;
            stats.Crits += critical ? 1 : 0;
            stats.Combo++;
            stats.BestCombo = Math.Max(stats.BestCombo, stats.Combo);
            victim.Stats.EndCombo();
        }
        else
        {
            stats.StrongerHits++;
        }
        return result;
    }

    /// <summary>As Game.ThrowRod.</summary>
    private void ThrowRod(Fighter thrower)
    {
        if (thrower.Vitality.IsDead || !thrower.Rod.TryThrow(_rules.RodCooldown))
        {
            return;
        }
        thrower.Stats.RodThrows++;
        Fighter other = thrower == _fighters[0] ? _fighters[1] : _fighters[0];
        if (thrower.Bobber is Bobber old && old.HookedId is null && old.LandedAt is null
            && (old.PositionAt(_time) - old.Origin).HorizontalLength < (other.Motor.Position - old.Origin).HorizontalLength)
        {
            thrower.Stats.RodThrowsLost++;
        }
        // Rounded like the cast message, as the game does.
        Vec3d origin = Vec3d.From(Bobber.CastOrigin(thrower.Eye, thrower.Yaw).ToGodot());
        Vec3d velocity = Vec3d.From(Bobber.CastVelocity(thrower.Yaw, thrower.Pitch, _random).ToGodot());
        thrower.Bobber = new Bobber(_world, thrower.Id, _time, origin, velocity);
        thrower.CheckedUntil = _time;
    }

    /// <summary>
    /// As Game.StepRods: each flying bobber looks for someone to catch. And as Game.UpdateBobber,
    /// one that has lain still or hung on someone long enough disappears.
    /// </summary>
    private void StepRods()
    {
        foreach (Fighter thrower in _fighters)
        {
            if (thrower.Bobber is not Bobber bobber || _time <= thrower.CheckedUntil)
            {
                continue;
            }
            if (bobber.HookedId is not null)
            {
                thrower.CheckedUntil = _time;
                thrower.Bobber = bobber.IsGoneAt(_time) ? null : bobber;
                continue;
            }
            double from = thrower.CheckedUntil;
            thrower.CheckedUntil = _time;
            _rodTargets.Clear();
            Fighter victim = thrower == _fighters[0] ? _fighters[1] : _fighters[0];
            _rodTargets.Add(new RodTarget(victim.Id, SeenFeet(victim)));
            // Catches first, then the bobber's place is checked, in the game's order (it checks
            // for catches every tick, and where the bobber is when drawing it).
            if (bobber.Catch(from, _time, _rodTargets) is { } hit
                && Resolve(thrower, victim, rod: true, critical: false, sprintKnockback: false) != HitResult.Ignored)
            {
                bobber.Hook(victim.Id, hit.Time);
                continue;
            }
            Vec3d b = bobber.PositionAt(_time);
            if ((b - thrower.Motor.Position).Length > Tuning.RodLineLength || b.Y < Tuning.VoidY || bobber.IsGoneAt(_time))
            {
                thrower.Bobber = null;
            }
        }
    }

    /// <summary>
    /// Where the other fighter sees <paramref name="target"/> now, and so where its clicks and
    /// bobber find them: the match's head start in the past, as the game shows its own bots.
    /// </summary>
    private Vec3d SeenFeet(Fighter target) => target.Trail.IsEmpty
        ? target.Motor.Position
        : target.Trail.PositionAt(_time - HeadStart.ViewDelay(_rules.HeadStart, owned: true, 0));

    private sealed class Fighter
    {
        public Fighter(int id, IDuelBrain brain, PlayerMotor motor, double yaw)
        {
            Id = id;
            Brain = brain;
            Motor = motor;
            Yaw = Mathf.Wrap((float)yaw, -180f, 180f);
        }

        public int Id { get; }
        public IDuelBrain Brain { get; }
        public PlayerMotor Motor { get; }
        public Vitality Vitality { get; } = new();
        public RodCooldown Rod { get; } = new();
        public ClickLockout Lockout { get; } = new();
        public MissPenalty Miss { get; } = new();
        public DuelSide Stats { get; } = new();
        public int Subtick { get; set; }
        public float Yaw { get; set; }
        public float Pitch { get; set; }
        public double HurtAt { get; set; } = double.NegativeInfinity;
        public bool HurtByRod { get; set; }
        /// <summary>Where its last sword knockback started, whether it's still being measured, and whether it cut it by clicking.</summary>
        public Vec3d KnockbackFrom { get; set; }
        public bool KnockbackTracked { get; set; }
        public bool KnockbackCut { get; set; }
        public Bobber? Bobber { get; set; }
        public double CheckedUntil { get; set; }
        public Vec3d Eye => Motor.Position + new Vec3d(0, Tuning.EyeHeight, 0);
        /// <summary>Where it has been, tick by tick, to be seen in the past.</summary>
        public BotPerception Trail { get; } = new();
        public bool CanAttack => !Vitality.IsDead && !Miss.Blocks && !Lockout.Blocks;
    }
}

/// <summary>Sword knockbacks one side took, split by whether it cut them by clicking back while flying.</summary>
public sealed class KnockbackTally
{
    public int Cut { get; set; }
    public double CutDistance { get; set; }
    public int Full { get; set; }
    public double FullDistance { get; set; }

    public double CutShare => Cut + Full > 0 ? (double)Cut / (Cut + Full) : 0;
    public double AverageCut => Cut > 0 ? CutDistance / Cut : 0;
    public double AverageFull => Full > 0 ? FullDistance / Full : 0;
    public double Average => Cut + Full > 0 ? (CutDistance + FullDistance) / (Cut + Full) : 0;

    /// <summary>"4.1 blocks (35% cut: 2.9 vs 4.8)"</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"{Average:0.0} blocks ({CutShare:P0} cut: {AverageCut:0.0} vs {AverageFull:0.0})");
}

/// <summary>Many duels between two kinds of bot, swapping sides, summed up.</summary>
public sealed class DuelSeries
{
    public string NameA { get; private init; } = "";
    public string NameB { get; private init; } = "";
    public int Duels { get; private set; }
    public int WinsA { get; private set; }
    public int WinsB { get; private set; }
    public double HitsA { get; private set; }
    public double HitsB { get; private set; }
    public double RodHitsA { get; private set; }
    public double RodHitsB { get; private set; }
    public double ComboA { get; private set; }
    public double ComboB { get; private set; }
    public int BestComboA { get; private set; }
    public int BestComboB { get; private set; }
    public double ClicksA { get; private set; }
    public double ClicksB { get; private set; }
    public double WastedA { get; private set; }
    public double WastedB { get; private set; }
    public double MissesA { get; private set; }
    public double MissesB { get; private set; }
    public double Seconds { get; private set; }
    public double InReachA { get; private set; }
    public double InReachB { get; private set; }
    public double OnTargetA { get; private set; }
    public double OnTargetB { get; private set; }
    public double ContinuesA { get; private set; }
    public double ContinuesB { get; private set; }
    public double ReversalsA { get; private set; }
    public double ReversalsB { get; private set; }
    /// <summary>Every combo each side landed, by length.</summary>
    public List<int> CombosA { get; } = new();
    public List<int> CombosB { get; } = new();

    /// <summary>
    /// A's combos summed up: average length, how many reach 4 hits, the length 1 in 10 reach,
    /// the longest, and the knockback B took.
    /// </summary>
    public string ComboSummaryA()
    {
        if (CombosA.Count == 0)
        {
            return "no combos";
        }
        var sorted = new List<int>(CombosA);
        sorted.Sort();
        double hits = 0;
        int fourPlus = 0;
        double hitsInLong = 0;
        foreach (int c in sorted)
        {
            hits += c;
            fourPlus += c >= 4 ? 1 : 0;
            hitsInLong += c >= 4 ? c : 0;
        }
        int p90 = sorted[(int)Math.Floor(0.9 * (sorted.Count - 1))];
        return string.Create(CultureInfo.InvariantCulture,
            $"{NameA} vs {NameB}: combos {hits / sorted.Count:0.0} hits on average, {(double)fourPlus / sorted.Count:P0} reach 4+ "
            + $"({hitsInLong / Math.Max(hits, 1):P0} of hits), 1 in 10 reach {p90}, longest {sorted[^1]}; "
            + $"longest per duel {ComboA / Math.Max(Duels, 1):0.0}; attacker wins {WinRateA:P0}; "
            + $"{NameB} lands {HitsB / Math.Max(Duels, 1):0.0} hits and {RodHitsB / Math.Max(Duels, 1):0.0} rods a duel; "
            + $"knockback taken {KnockbackB}");
    }

    /// <summary>The knockback each side took: how often it cut it by clicking back, and how far it flew either way.</summary>
    public KnockbackTally KnockbackA { get; } = new();
    public KnockbackTally KnockbackB { get; } = new();

    /// <summary>After A lands a full hit, the chance A also lands the next one.</summary>
    public double KeepRateA => ContinuesA + ReversalsB > 0 ? ContinuesA / (ContinuesA + ReversalsB) : 0;
    public double KeepRateB => ContinuesB + ReversalsA > 0 ? ContinuesB / (ContinuesB + ReversalsA) : 0;

    public double WinRateA => Duels > 0 ? (WinsA + 0.5 * (Duels - WinsA - WinsB)) / Duels : 0;

    /// <summary>
    /// Fights <paramref name="duels"/> duels between brains made by <paramref name="makeA"/> and
    /// <paramref name="makeB"/> (from a seed), with seeds from <paramref name="seed"/>.
    /// </summary>
    public static DuelSeries Run(
        Func<int, IDuelBrain> makeA, Func<int, IDuelBrain> makeB, MatchRules rules, int duels, int seed = 1)
    {
        var series = new DuelSeries { NameA = makeA(0).Name, NameB = makeB(0).Name };
        for (int i = 0; i < duels; i++)
        {
            int s = seed * 1000 + i;
            DuelResult r = BotDuel.Fight(makeA(s * 2 + 1), makeB(s * 2 + 2), rules, s);
            series.Add(r);
        }
        return series;
    }

    private void Add(DuelResult r)
    {
        Duels++;
        WinsA += r.Winner == 1 ? 1 : 0;
        WinsB += r.Winner == 2 ? 1 : 0;
        HitsA += r.A.Hits;
        HitsB += r.B.Hits;
        RodHitsA += r.A.RodHits;
        RodHitsB += r.B.RodHits;
        ComboA += r.A.BestCombo;
        ComboB += r.B.BestCombo;
        BestComboA = Math.Max(BestComboA, r.A.BestCombo);
        CombosA.AddRange(r.A.ComboLengths);
        CombosB.AddRange(r.B.ComboLengths);
        BestComboB = Math.Max(BestComboB, r.B.BestCombo);
        ClicksA += r.A.Clicks;
        ClicksB += r.B.Clicks;
        WastedA += r.A.WastedClicks;
        WastedB += r.B.WastedClicks;
        MissesA += r.A.Misses;
        MissesB += r.B.Misses;
        Seconds += r.Seconds;
        InReachA += r.A.TicksInReach;
        InReachB += r.B.TicksInReach;
        OnTargetA += r.A.TicksOnTarget;
        OnTargetB += r.B.TicksOnTarget;
        ContinuesA += r.A.Continues;
        ContinuesB += r.B.Continues;
        ReversalsA += r.A.Reversals;
        ReversalsB += r.B.Reversals;
        foreach ((DuelSide side, KnockbackTally tally) in new[] { (r.A, KnockbackA), (r.B, KnockbackB) })
        {
            tally.Cut += side.KnockbacksCut;
            tally.CutDistance += side.CutKnockbackDistance;
            tally.Full += side.KnockbacksFull;
            tally.FullDistance += side.FullKnockbackDistance;
        }
    }

    public override string ToString()
    {
        var s = new StringBuilder();
        double n = Math.Max(Duels, 1);
        s.Append(CultureInfo.InvariantCulture, $"{NameA} vs {NameB}: {Duels} duels, wins {WinsA}-{WinsB} ({WinRateA:P0}), ");
        s.Append(CultureInfo.InvariantCulture, $"{Seconds / n:0.0} s each; hits {HitsA / n:0.0}-{HitsB / n:0.0}, ");
        s.Append(CultureInfo.InvariantCulture, $"rod hits {RodHitsA / n:0.0}-{RodHitsB / n:0.0}, ");
        s.Append(CultureInfo.InvariantCulture, $"longest combo avg {ComboA / n:0.0}-{ComboB / n:0.0} (best {BestComboA}-{BestComboB}), ");
        s.Append(CultureInfo.InvariantCulture, $"clicks {ClicksA / n:0}-{ClicksB / n:0}, ");
        s.Append(CultureInfo.InvariantCulture, $"wasted {Pct(WastedA, ClicksA)}-{Pct(WastedB, ClicksB)} ");
        s.Append(CultureInfo.InvariantCulture, $"(into protection {Pct(WastedA - MissesA, ClicksA)}-{Pct(WastedB - MissesB, ClicksB)}), ");
        s.Append(CultureInfo.InvariantCulture, $"on target in reach {Pct(OnTargetA, InReachA)}-{Pct(OnTargetB, InReachB)}, ");
        s.Append(CultureInfo.InvariantCulture, $"keeps combo {KeepRateA:P0}-{KeepRateB:P0}, ");
        s.Append(CultureInfo.InvariantCulture, $"knockback taken {KnockbackA}-{KnockbackB}");
        return s.ToString();
    }

    private static string Pct(double part, double whole) =>
        whole > 0 ? (part / whole).ToString("P0", CultureInfo.InvariantCulture) : "-";
}
