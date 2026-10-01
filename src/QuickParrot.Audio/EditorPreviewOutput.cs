using QuickParrot.Core.Editing;

namespace QuickParrot.Audio;

/// <summary>One stretch of editor audio playing to one device, converted to that device's mix format.</summary>
internal sealed class EditorPreviewOutput : IDisposable
{
    private readonly WasapiOutput _output;
    private readonly BufferSampleProvider _source;
    private readonly int _startFrame;
    private readonly int _endFrame;

    private EditorPreviewOutput(WasapiOutput output, EditableAudio audio, BufferSampleProvider source, int startFrame, int endFrame)
    {
        _output = output;
        Audio = audio;
        _source = source;
        _startFrame = startFrame;
        _endFrame = endFrame;
    }

    /// <summary>Completes when playback ends by itself, with the error if it failed; never once disposed.</summary>
    public Task<Exception?> Stopped => _output.Stopped;

    public EditableAudio Audio { get; }

    /// <summary>The source frame being heard: the device clock where available, else how far the source has been read.</summary>
    public int PositionFrame => _output.PlayedSeconds is { } seconds
        ? Math.Min(_endFrame, _startFrame + (int)(seconds * Audio.SampleRate))
        : _source.FramePosition;

    public static EditorPreviewOutput Open(string deviceId, EditableAudio audio, int startFrame, int endFrame, float gain)
    {
        var source = BufferSampleProvider.Of(audio, startFrame, endFrame, gain);
        return new EditorPreviewOutput(WasapiOutput.Open(deviceId, source), audio, source, startFrame, endFrame);
    }

    public void Play() => _output.Play();

    // Stops in the background so the UI never waits on the audio thread winding down.
    public void Dispose() => _output.Dispose();
}
