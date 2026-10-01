using QuickParrot.Core.Common;

namespace QuickParrot.Core.Grabs;

/// <summary>
/// Keeps pending grabs as 32-bit float WAV files in one folder, by default %LOCALAPPDATA%\QuickParrot\Grabs. Only the
/// newest <see cref="MaxGrabs"/> are kept: saving one more deletes the oldest. Thread-safe.
/// </summary>
public sealed class FilePendingGrabStore : IPendingGrabStore
{
    public const int DefaultMaxGrabs = 20;
    private const string Extension = ".wav";
    private const string TempExtension = ".tmp";

    private readonly object _lock = new();
    private bool _cleanedTempFiles;

    public FilePendingGrabStore(string directory, int maxGrabs = DefaultMaxGrabs)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxGrabs);
        Directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        MaxGrabs = maxGrabs;
    }

    public event Action? Changed;

    public static string DefaultDirectory => AppDataPaths.Local("Grabs");

    private string Directory { get; }

    private int MaxGrabs { get; }

    public IReadOnlyList<PendingGrab> List()
    {
        lock (_lock)
            return ListLocked();
    }

    // Written to a temp file and renamed into place, so a crash mid-write never leaves a truncated grab.
    public PendingGrab Save(ReadOnlySpan<float> samples, int sampleRate, int channels, DateTimeOffset grabbedAt)
    {
        PendingGrab saved;
        lock (_lock)
        {
            System.IO.Directory.CreateDirectory(Directory);
            CleanTempFilesOnce();
            var id = UniqueId(grabbedAt);
            var path = PathFor(id);
            var tempPath = Path.ChangeExtension(path, TempExtension);
            try
            {
                using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
                    WavFile.WriteFloat32(stream, samples, sampleRate, channels);

                File.Move(tempPath, path);
            }
            catch
            {
                TryDelete(tempPath);
                throw;
            }

            saved = Describe(path) ?? throw new IOException($"The grab just saved to {path} couldn't be read back.");
            Prune(saved);
        }

        Changed?.Invoke();
        return saved;
    }

    public void Delete(PendingGrab grab)
    {
        lock (_lock)
        {
            var path = Path.GetFullPath(grab.FilePath);
            if (!IsOwnFile(path))
                throw new ArgumentException($"{grab.FilePath} isn't a pending grab.", nameof(grab));

            File.Delete(path);
        }

        Changed?.Invoke();
    }

    private List<PendingGrab> ListLocked()
    {
        CleanTempFilesOnce();
        if (!System.IO.Directory.Exists(Directory))
            return [];

        var grabs = new List<(PendingGrab Grab, int Sequence)>();
        foreach (var path in System.IO.Directory.EnumerateFiles(Directory, GrabFileName.Prefix + "*" + Extension))
        {
            if (GrabFileName.TryParse(Path.GetFileNameWithoutExtension(path), out _, out var sequence)
                && Describe(path) is { } grab)
            {
                grabs.Add((grab, sequence));
            }
        }

        return grabs.OrderBy(g => g.Grab.GrabbedAt).ThenBy(g => g.Sequence).Select(g => g.Grab).ToList();
    }

    private void Prune(PendingGrab keep)
    {
        var grabs = ListLocked();
        for (var i = 0; grabs.Count - i > MaxGrabs; i++)
        {
            if (grabs[i] != keep)
                TryDelete(grabs[i].FilePath);
        }
    }

    private string UniqueId(DateTimeOffset grabbedAt)
    {
        for (var sequence = 1; ; sequence++)
        {
            var id = GrabFileName.Format(grabbedAt, sequence);
            var path = PathFor(id);
            if (!File.Exists(path) && !File.Exists(Path.ChangeExtension(path, TempExtension)))
                return id;
        }
    }

    // Leftovers from a crash mid-save; within this process saves are serialized, so none are in progress.
    private void CleanTempFilesOnce()
    {
        if (_cleanedTempFiles || !System.IO.Directory.Exists(Directory))
            return;

        _cleanedTempFiles = true;
        foreach (var path in System.IO.Directory.EnumerateFiles(Directory, GrabFileName.Prefix + "*" + TempExtension))
            TryDelete(path);
    }

    private static PendingGrab? Describe(string path)
    {
        var id = Path.GetFileNameWithoutExtension(path);
        if (!GrabFileName.TryParse(id, out var grabbedAt, out _))
            return null;

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 512);
            return WavFile.TryReadInfo(stream) is { } info ? new PendingGrab(id, path, grabbedAt, info.Duration) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private bool IsOwnFile(string path) =>
        string.Equals(Path.GetDirectoryName(path), Directory, StringComparison.OrdinalIgnoreCase)
        && Path.GetExtension(path).Equals(Extension, StringComparison.OrdinalIgnoreCase)
        && GrabFileName.TryParse(Path.GetFileNameWithoutExtension(path), out _, out _);

    private string PathFor(string id) => Path.Combine(Directory, id + Extension);

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Best effort: it'll be pruned or cleaned up next time.
        }
    }
}
