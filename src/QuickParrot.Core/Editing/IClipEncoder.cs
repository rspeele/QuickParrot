namespace QuickParrot.Core.Editing;

/// <summary>Writes finished clips into the library.</summary>
public interface IClipEncoder
{
    /// <summary>
    /// Encodes <paramref name="audio"/> into <paramref name="folder"/> as "<paramref name="fileStem"/>.ext", adding
    /// " (2)" etc. rather than overwriting. The file appears complete or not at all. Blocking; call off the UI thread.
    /// </summary>
    SavedClip Save(EditableAudio audio, string folder, string fileStem, CancellationToken cancellationToken);
}

/// <param name="Warning">Set when the clip was saved, but not as intended (e.g. WAV because MP3 encoding is unavailable).</param>
public sealed record SavedClip(string FullPath, string? Warning = null)
{
    public string FileName => Path.GetFileName(FullPath);
}
