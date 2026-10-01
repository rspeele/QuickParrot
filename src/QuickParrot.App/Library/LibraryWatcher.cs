using System.IO;
using QuickParrot.Core.Library;

namespace QuickParrot.App.Library;

/// <summary>
/// Watches a library root recursively for folder/file creates, deletes and renames, debouncing bursts (see
/// <see cref="LibraryChangeDebouncer"/>) into one <see cref="Changed"/> event. Raised on a background thread.
/// </summary>
public sealed class LibraryWatcher : IDisposable
{
    private readonly LibraryChangeDebouncer _debouncer;
    private readonly FileSystemWatcher? _watcher;

    public LibraryWatcher(string root, TimeProvider time, TimeSpan? delay = null)
    {
        _debouncer = new LibraryChangeDebouncer(() => Changed?.Invoke(), delay ?? LibraryChangeDebouncer.DefaultDelay, time);
        FileSystemWatcher? watcher = null;
        try
        {
            watcher = new FileSystemWatcher(root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName,
                InternalBufferSize = 64 * 1024,
            };
            watcher.Created += OnEvent;
            watcher.Deleted += OnEvent;
            watcher.Renamed += OnRenamed;
            watcher.Error += (_, _) => _debouncer.Signal(); // buffer overflow or similar: play it safe and refresh
            watcher.EnableRaisingEvents = true;
            _watcher = watcher;
        }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            watcher?.Dispose(); // root missing, unreadable, or a share without change notifications: no live refresh
        }
    }

    /// <summary>Fires ~300 ms after the library root settles down, on a background thread.</summary>
    public event Action? Changed;

    public void Dispose()
    {
        _watcher?.Dispose();
        _debouncer.Dispose();
    }

    private void OnEvent(object sender, FileSystemEventArgs e)
    {
        if (!ClipTempFile.IsMatch(Path.GetFileName(e.Name)))
            _debouncer.Signal();
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        var fromTemp = ClipTempFile.IsMatch(Path.GetFileName(e.OldName));
        var toTemp = ClipTempFile.IsMatch(Path.GetFileName(e.Name));
        if (!fromTemp || !toTemp)
            _debouncer.Signal();
    }
}
