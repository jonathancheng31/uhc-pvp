using System.Collections.Generic;
using System.Linq;
using Godot;
using UhcPvp.Core;
using UhcPvp.Movement;

namespace UhcPvp.Gameplay;

/// <summary>
/// Footsteps and landings for every player (you, bots and other people), worked out each frame
/// from where they're drawn (<see cref="Footsteps"/>), so they sound exactly as they're seen to
/// move. Others' come from their feet, so you can hear where they are; yours are quieter and in
/// your head. Runs after the players have moved this frame.
/// </summary>
public partial class GameAudio : Node
{
    private readonly Dictionary<int, Footsteps> _feet = new();
    private readonly List<Box> _scratch = new();

    public Game Game { get; init; } = null!;

    public override void _Ready()
    {
        // After every player (priority 0) has been placed for this frame.
        ProcessPriority = 5;
        Sounds.SetUpBus();
    }

    public override void _ExitTree() => Sounds.Release();

    public override void _Process(double delta)
    {
        foreach (Player player in Game.Players)
        {
            if (!_feet.TryGetValue(player.PeerId, out Footsteps? steps))
            {
                steps = new Footsteps();
                _feet[player.PeerId] = steps;
            }
            Vec3d feet = player.ShownFeet;
            FootstepEvent e = steps.Update(feet, Standing(feet), player.Sneaking, player.IsDead, delta);
            if (e.Step)
            {
                Play(player, SoundBank.Step, 0);
            }
            if (e.LandedFrom > 0)
            {
                Play(player, SoundBank.Land, SoundBank.LandingDb(e.LandedFrom));
            }
        }
        if (_feet.Count > Game.PlayerCount)
        {
            foreach (int gone in _feet.Keys.Where(id => Game.Find(id) is null).ToList())
            {
                _feet.Remove(gone);
            }
        }
    }

    private void Play(Player player, SoundSpec sound, double volumeDb)
    {
        if (player.IsLocal)
        {
            Sounds.PlayFlat(Game, sound, volumeDb + SoundBank.OwnStepsDb);
        }
        else
        {
            Sounds.PlayAt(Game, sound, player.Position, volumeDb);
        }
    }

    /// <summary>Whether a player drawn at <paramref name="feet"/> is resting on a block.</summary>
    private bool Standing(Vec3d feet)
    {
        const double probe = 0.01;
        Box box = Box.ForPlayer(feet, Tuning.PlayerWidth, Tuning.PlayerHeight);
        return Game.World.Clip(box, 0, -probe, 0, _scratch).Y > -probe + 1e-9;
    }
}
