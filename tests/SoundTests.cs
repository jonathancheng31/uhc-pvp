using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using UhcPvp.Combat;
using UhcPvp.Core;
using UhcPvp.Movement;
using UhcPvp.World;

namespace UhcPvp.Tests;

/// <summary>
/// The sound effects: every file is there and mastered alike (loudness, no clipping, starting
/// at once and ending cleanly), variation never repeats a file back to back, and footsteps,
/// landings and the bobber's landing fire when they should.
/// </summary>
public class SoundTests : TestCase
{
    /// <summary>tools/gen_sounds.py's TARGET_LUFS and CEILING_DB.</summary>
    private const double TargetLufs = -14.0;
    private const double CeilingDb = -1.0;
    private const int Rate = 44100;

    public void TestEveryFileIsThereAndMasteredAlike()
    {
        foreach (SoundSpec sound in SoundBank.All)
        {
            for (int i = 0; i < sound.Variants; i++)
            {
                string path = sound.PathOf(i);
                double[]? x = ReadWav(path);
                if (x is null)
                {
                    Failures.Add($"{path}: missing or not 16-bit mono {Rate} Hz");
                    continue;
                }
                double peak = x.Max(Math.Abs);
                Check(20 * Math.Log10(peak) <= CeilingDb + 0.05, $"{path}: peak {20 * Math.Log10(peak):0.0} dBFS, over the ceiling");
                CheckNear(Loudness(x), TargetLufs, 0.75, $"{path}: loudness (LUFS, loudest 100 ms)");
                // It starts at once, so it lands on the frame it's played...
                int onset = Array.FindIndex(x, s => Math.Abs(s) > peak * 0.1);
                Check(onset <= Rate * 0.004, $"{path}: takes {onset * 1000.0 / Rate:0.0} ms to start");
                // ...without a click, and ends without one.
                Check(Math.Abs(x[0]) < 0.01 && Math.Abs(x[^1]) < 0.01, $"{path}: starts or ends with a jump (a click)");
                Check(x.Length <= Rate / 2, $"{path}: longer than half a second");
            }
        }
    }

    public void TestVariationNeverRepeatsAFileBackToBack()
    {
        var random = new Random(5);
        foreach (SoundSpec sound in SoundBank.All)
        {
            var variation = new SoundVariation(sound, random);
            int last = -1;
            var round = new HashSet<int>();
            double pitchSum = 0;
            const int plays = 2000;
            for (int n = 0; n < plays; n++)
            {
                (int variant, double pitch, double volume) = variation.Next();
                Check(variant != last, $"{sound.Name}: the same file twice in a row");
                Check(variant >= 0 && variant < sound.Variants, $"{sound.Name}: no such file {variant}");
                Check(Math.Abs(pitch - 1) <= sound.PitchSpread + 1e-12, $"{sound.Name}: pitch {pitch} out of range");
                Check(Math.Abs(volume) <= sound.VolumeSpreadDb + 1e-12, $"{sound.Name}: volume {volume} out of range");
                Check(round.Add(variant), $"{sound.Name}: a file came back before the round was over");
                if (round.Count == sound.Variants)
                {
                    round.Clear();
                }
                last = variant;
                pitchSum += pitch;
            }
            CheckNear(pitchSum / plays, 1, 0.01, $"{sound.Name}: pitch centred on the file's own");
        }
    }

    public void TestMixKeepsTheFamilyInOrder()
    {
        // A sword hit is hurt + the attacker's hit (+ crit): the victim's sound leads, the
        // attacker's sits just under it, the crit on top of both a little lower.
        Check(SoundBank.Hit.VolumeDb < SoundBank.Hurt.VolumeDb, "the hit sits under the hurt it plays with");
        Check(SoundBank.Crit.VolumeDb < SoundBank.Hurt.VolumeDb, "the crit sits under the hurt");
        Check(SoundBank.Swing.VolumeDb + SoundBank.OwnSwingDb <= SoundBank.Hurt.VolumeDb - 10, "your swings stay well under the hits");
        Check(SoundBank.Step.VolumeDb <= SoundBank.Hurt.VolumeDb - 10, "other players' footsteps stay well under the fight");
        Check(SoundBank.Step.VolumeDb + SoundBank.OwnStepsDb <= SoundBank.Hurt.VolumeDb - 18, "your own footsteps are subtle");
        Check(SoundBank.Hit.UnitSize == 0, "your hit is in your head, not in the world");
        foreach (SoundSpec sound in SoundBank.All)
        {
            Check(sound.MaxVoices >= 1 && sound.Variants >= 4, $"{sound.Name}: needs voices and at least 4 files");
            Check(sound.UnitSize == 0 || sound.MaxDistance > sound.UnitSize, $"{sound.Name}: heard further than its full-volume range");
        }
    }

    public void TestComboPitchClimbsThenHolds()
    {
        CheckNear(SoundBank.ComboPitch(1), 1, 1e-12, "the first hit at its own pitch");
        Check(SoundBank.ComboPitch(2) > SoundBank.ComboPitch(1), "the second hit higher");
        double top = SoundBank.ComboPitch(SoundBank.ComboPitchSteps + 1);
        CheckNear(SoundBank.ComboPitch(20), top, 1e-12, "then it stays there");
        Check(top <= 1.15, "never more than a little higher");
        CheckNear(SoundBank.ComboPitch(0), 1, 1e-12, "a hit outside a combo at its own pitch");
    }

    public void TestLandingsAreLouderForBiggerFalls()
    {
        CheckNear(SoundBank.LandingDb(1.25), 0, 1e-9, "a sprint jump's landing is the base");
        Check(SoundBank.LandingDb(0.4) < 0, "a little hop is softer");
        Check(SoundBank.LandingDb(5) > SoundBank.LandingDb(2.5), "a bigger drop is louder");
        CheckNear(SoundBank.LandingDb(100), 6, 1e-9, "up to 6 dB louder");
        CheckNear(SoundBank.LandingDb(0), -5, 1e-9, "never silent");
    }

    public void TestFootstepsFollowTheStride()
    {
        var steps = new Footsteps();
        const double dt = 1 / 60.0;
        const double speed = 5.612; // sprinting, blocks per second
        var times = new List<double>();
        for (int frame = 0; frame < 240; frame++)
        {
            double t = frame * dt;
            if (steps.Update(new Vec3d(t * speed, 4, 0), grounded: true, sneaking: false, dead: false, dt).Step)
            {
                times.Add(t);
            }
        }
        // The first step comes quickly after starting, then one every stride.
        Check(times.Count > 0 && times[0] <= Footsteps.FirstStep / speed + 2 * dt, "the first step comes soon after starting");
        for (int i = 1; i < times.Count; i++)
        {
            CheckNear(times[i] - times[i - 1], Footsteps.Stride / speed, 1.5 * dt, $"step {i} a stride after the last");
        }
        CheckNear(times.Count, 1 + (4 * speed - Footsteps.FirstStep) / Footsteps.Stride, 1, "steps in 4 s of sprinting");
    }

    public void TestSneakingAndStandingAreSilent()
    {
        var sneaker = new Footsteps();
        var stander = new Footsteps();
        int count = 0;
        for (int frame = 0; frame < 120; frame++)
        {
            count += sneaker.Update(new Vec3d(frame * 0.03, 4, 0), true, sneaking: true, dead: false, 1 / 60.0).Step ? 1 : 0;
            count += stander.Update(new Vec3d(3.6, 4, 0), true, sneaking: false, dead: false, 1 / 60.0).Step ? 1 : 0;
        }
        CheckEqual(count, 0, "no steps while sneaking or standing still");
    }

    public void TestJumpLandsOnceWithItsHeight()
    {
        var steps = new Footsteps();
        const double dt = 1 / 60.0;
        double landed = 0;
        int events = 0;
        // Walk, then a jump arc 1.25 blocks high and 0.6 s long, then walk on.
        for (int frame = 0; frame < 120; frame++)
        {
            double t = frame * dt;
            double air = t - 0.5;
            bool flying = air > 0 && air < 0.6;
            double y = 4 + (flying ? 1.25 * (1 - Math.Pow(air / 0.3 - 1, 2)) : 0);
            FootstepEvent e = steps.Update(new Vec3d(t * 5, y, 0), !flying, false, false, dt);
            if (e.LandedFrom > 0)
            {
                landed = e.LandedFrom;
                events++;
                CheckNear(t, 1.1, dt + 1e-9, "lands the frame it touches down");
            }
            if (flying && e.Step)
            {
                Failures.Add("a step in mid-air");
            }
        }
        CheckEqual(events, 1, "one landing");
        CheckNear(landed, 1.25, 0.01, "the fall height is the jump's");
    }

    public void TestRespawnsAndBumpsMakeNoSound()
    {
        var steps = new Footsteps();
        steps.Update(new Vec3d(10, 4, 10), true, false, false, 1 / 60.0);
        FootstepEvent jump = steps.Update(new Vec3d(30, 4, 30), true, false, false, 1 / 60.0);
        CheckEqual(jump, FootstepEvent.None, "teleporting isn't walking");
        steps.Update(new Vec3d(30, 4.1, 30), false, false, false, 1 / 60.0);
        FootstepEvent bump = steps.Update(new Vec3d(30, 4, 30), true, false, false, 1 / 60.0);
        CheckEqual(bump.LandedFrom, 0.0, "a tiny drop lands silently");
        CheckEqual(steps.Update(new Vec3d(31, 4, 30), true, false, dead: true, 1 / 60.0), FootstepEvent.None, "the dead are silent");
    }

    public void TestBobberLandingTimeIsWhenItsDrawnOnTheGround()
    {
        var world = new VoxelWorld(new Vector3I(64, 40, 64));
        world.Fill(new Vector3I(0, 0, 0), new Vector3I(63, 3, 63), Block.Stone);
        const double castTime = 10.0;
        var feet = new Vec3d(32, 4, 32);
        Vec3d eye = feet + new Vec3d(0, Tuning.EyeHeight, 0);
        var bobber = new Bobber(world, 1, castTime, Bobber.CastOrigin(eye, 0), Bobber.CastVelocity(0, 10, Vec3d.Zero));
        Check(bobber.LandedAt is null, "still flying at the cast");
        double? landedAt = null;
        for (double t = castTime; t < castTime + 4 && landedAt is null; t += 1 / 60.0)
        {
            bobber.PositionAt(t);
            landedAt = bobber.LandedAt;
        }
        if (landedAt is not double at)
        {
            Failures.Add("never landed");
            return;
        }
        CheckNear(bobber.PositionAt(at).Y, 4, 1e-6, "on the floor at its landing time");
        Check(bobber.PositionAt(at - 0.02).Y > 4 + 1e-4, "still above it just before");
        CheckNear((at - castTime) * Tuning.McTickRate, Math.Round((at - castTime) * Tuning.McTickRate), 1e-9, "on a Minecraft tick");
        bobber.Release(at + 1, new Vec3d(32, 8, 40));
        Check(bobber.LandedAt is null, "dropped again: in the air until it lands again");
    }

    /// <summary>Samples of a 16-bit mono WAV in the project, or null if it isn't one.</summary>
    private static double[]? ReadWav(string path)
    {
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file is null)
        {
            return null;
        }
        byte[] bytes = file.GetBuffer((long)file.GetLength());
        if (bytes.Length < 44 || System.Text.Encoding.ASCII.GetString(bytes, 0, 4) != "RIFF")
        {
            return null;
        }
        int pos = 12;
        bool format = false;
        while (pos + 8 <= bytes.Length)
        {
            string id = System.Text.Encoding.ASCII.GetString(bytes, pos, 4);
            int size = BitConverter.ToInt32(bytes, pos + 4);
            if (id == "fmt ")
            {
                format = BitConverter.ToInt16(bytes, pos + 8) == 1 && BitConverter.ToInt16(bytes, pos + 10) == 1
                    && BitConverter.ToInt32(bytes, pos + 12) == Rate && BitConverter.ToInt16(bytes, pos + 22) == 16;
            }
            else if (id == "data" && format)
            {
                var samples = new double[Math.Min(size, bytes.Length - pos - 8) / 2];
                for (int i = 0; i < samples.Length; i++)
                {
                    samples[i] = BitConverter.ToInt16(bytes, pos + 8 + 2 * i) / 32768.0;
                }
                return samples;
            }
            pos += 8 + size + (size & 1);
        }
        return null;
    }

    /// <summary>Loudness of the loudest 100 ms, K-weighted like ITU-R BS.1770 (as tools/gen_sounds.py measures it).</summary>
    private static double Loudness(double[] x)
    {
        double[] y = KWeighted(x);
        int window = Rate / 10;
        var squares = new double[y.Length + window + 1];
        for (int i = 0; i < y.Length; i++)
        {
            squares[i + 1] = squares[i] + y[i] * y[i];
        }
        for (int i = y.Length; i < squares.Length - 1; i++)
        {
            squares[i + 1] = squares[i];
        }
        double loudest = 0;
        for (int start = 0; start + window < squares.Length; start += Rate / 500)
        {
            loudest = Math.Max(loudest, (squares[start + window] - squares[start]) / window);
        }
        return -0.691 + 10 * Math.Log10(loudest + 1e-20);
    }

    private static double[] KWeighted(double[] x)
    {
        double a = Math.Pow(10, 3.99984385397 / 40);
        double w0 = 2 * Math.PI * 1681.9744509555319 / Rate;
        double alpha = Math.Sin(w0) / (2 * 0.7071752369554193);
        double c = Math.Cos(w0);
        double sq = 2 * Math.Sqrt(a) * alpha;
        double[] shelf = Biquad(x,
            a * ((a + 1) + (a - 1) * c + sq), -2 * a * ((a - 1) + (a + 1) * c), a * ((a + 1) + (a - 1) * c - sq),
            (a + 1) - (a - 1) * c + sq, 2 * ((a - 1) - (a + 1) * c), (a + 1) - (a - 1) * c - sq);
        w0 = 2 * Math.PI * 38.13547087613982 / Rate;
        alpha = Math.Sin(w0) / (2 * 0.5003270373253953);
        c = Math.Cos(w0);
        return Biquad(shelf, (1 + c) / 2, -(1 + c), (1 + c) / 2, 1 + alpha, -2 * c, 1 - alpha);
    }

    private static double[] Biquad(double[] x, double b0, double b1, double b2, double a0, double a1, double a2)
    {
        var y = new double[x.Length];
        double x1 = 0, x2 = 0, y1 = 0, y2 = 0;
        for (int i = 0; i < x.Length; i++)
        {
            double v = (b0 * x[i] + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2) / a0;
            x2 = x1;
            x1 = x[i];
            y2 = y1;
            y1 = v;
            y[i] = v;
        }
        return y;
    }
}
