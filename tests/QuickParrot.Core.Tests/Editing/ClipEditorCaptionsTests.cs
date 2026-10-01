using QuickParrot.Core.Editing;
using QuickParrot.Core.Grabs;

namespace QuickParrot.Core.Tests.Editing;

public class ClipEditorCaptionsTests
{
    private static EditableAudio TwoSeconds(string? label) => new(new float[96000], 48000, 1, sourceLabel: label);

    [Fact]
    public void WithASourceLabel_NamesIt()
    {
        Assert.Equal("Edit clip — Grab at 21:04", ClipEditorCaptions.Title(TwoSeconds("Grab at 21:04")));
        Assert.Equal("Grab at 21:04  ·  0:02.000 captured", ClipEditorCaptions.Header(TwoSeconds("Grab at 21:04")));
    }

    [Fact]
    public void WithoutASourceLabel_IsGeneric()
    {
        Assert.Equal("Edit clip", ClipEditorCaptions.Title(TwoSeconds("")));
        Assert.Equal("0:02.000 captured", ClipEditorCaptions.Header(TwoSeconds(null)));
    }
}

public class GrabAudioTests
{
    [Fact]
    public void Read_KeepsTheFormat_AndLabelsItWithTheLocalGrabTime()
    {
        var grabbedAt = new DateTimeOffset(2026, 1, 2, 21, 4, 0, TimeSpan.Zero);
        using var wav = new MemoryStream();
        WavFile.WriteFloat32(wav, [0.5f, -0.5f, 0.25f, -0.25f], 44100, 2);
        wav.Position = 0;

        var audio = GrabAudio.Read(wav, grabbedAt);

        Assert.Equal((44100, 2, 2), (audio.SampleRate, audio.Channels, audio.FrameCount));
        Assert.Equal([0.5f, -0.5f, 0.25f, -0.25f], audio.Samples.ToArray());
        Assert.Equal($"Grab at {grabbedAt.ToLocalTime():HH:mm}", audio.SourceLabel);
    }

    [Fact]
    public void Read_RejectsNonWavData()
    {
        using var junk = new MemoryStream(new byte[64]);

        Assert.Throws<InvalidDataException>(() => GrabAudio.Read(junk, DateTimeOffset.Now));
    }
}
