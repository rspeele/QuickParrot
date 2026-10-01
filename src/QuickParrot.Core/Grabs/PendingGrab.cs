namespace QuickParrot.Core.Grabs;

/// <summary>A replay-buffer grab saved to disk, waiting to be trimmed into a clip (or deleted).</summary>
/// <param name="Id">The file name without extension, unique within the store.</param>
public sealed record PendingGrab(string Id, string FilePath, DateTimeOffset GrabbedAt, TimeSpan Duration);

public interface IPendingGrabStore
{
    /// <summary>Raised after a grab is saved or deleted, on the thread that did it.</summary>
    event Action? Changed;

    /// <summary>Oldest first. Unreadable files are skipped.</summary>
    IReadOnlyList<PendingGrab> List();

    /// <summary>Saves interleaved float frames; may delete the oldest grabs to stay under the store's limit.</summary>
    PendingGrab Save(ReadOnlySpan<float> samples, int sampleRate, int channels, DateTimeOffset grabbedAt);

    /// <summary>Does nothing if the grab is already gone.</summary>
    void Delete(PendingGrab grab);
}
