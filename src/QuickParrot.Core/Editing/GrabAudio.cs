using QuickParrot.Core.Grabs;

namespace QuickParrot.Core.Editing;

/// <summary>Reads a pending grab back as audio for the clip editor.</summary>
public static class GrabAudio
{
    /// <summary>Blocking file read; may throw IO, <see cref="InvalidDataException"/> or <see cref="NotSupportedException"/>.</summary>
    public static EditableAudio Load(PendingGrab grab)
    {
        using var stream = new FileStream(grab.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Read(stream, grab.GrabbedAt);
    }

    public static EditableAudio Read(Stream wav, DateTimeOffset grabbedAt)
    {
        var audio = WavFile.ReadFloat32(wav);
        return new EditableAudio(audio.Samples, audio.SampleRate, audio.Channels, sourceLabel: $"Grab at {grabbedAt.ToLocalTime():HH:mm}");
    }
}
