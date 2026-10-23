using Godot;
using UhcPvp.Autoload;

namespace UhcPvp.Gameplay;

/// <summary>
/// The --record-audio option: records everything the game plays (the Master bus, after its
/// limiter) and saves it as a WAV file when the game quits, so the mix can be checked without
/// listening (tools/gen_sounds.py --analyze).
/// </summary>
public partial class AudioRecorder : Node
{
    private AudioEffectRecord _record = null!;
    private bool _saved;

    public string FilePath { get; init; } = "";

    public override void _Ready()
    {
        Sounds.SetUpBus();
        _record = new AudioEffectRecord();
        AudioServer.AddBusEffect(AudioServer.GetBusIndex("Master"), _record);
        _record.SetRecordingActive(true);
        LaunchArgs.Instance.Quitting += Save;
    }

    public override void _ExitTree()
    {
        LaunchArgs.Instance.Quitting -= Save;
        Save();
    }

    private void Save()
    {
        if (_saved || _record.GetRecording() is not AudioStreamWav recording)
        {
            return;
        }
        _saved = true;
        Error error = recording.SaveToWav(FilePath);
        GD.Print($"[audio] recorded {recording.GetLength():0.0} s to {FilePath} ({error})");
    }
}
