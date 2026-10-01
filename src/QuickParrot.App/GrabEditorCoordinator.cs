using System.IO;
using QuickParrot.App.Editor;
using QuickParrot.Core.Editing;
using QuickParrot.Core.Grabs;
using QuickParrot.Core.Library;
using QuickParrot.Core.Settings;

namespace QuickParrot.App;

/// <summary>What editing a grab needs beyond the library, settings and status line.</summary>
/// <param name="OpenFolderSource">Opens a library root for reading; folder scans run off the UI thread.</param>
/// <param name="CreateNamer">The editor's "Suggest name" function, or null when naming isn't configured.</param>
/// <param name="ShowWindow">Shows an editor window for a view model.</param>
public sealed record GrabEditorServices(
    IPendingGrabStore GrabStore,
    IEditorPreview Preview,
    IClipEncoder Encoder,
    Func<string, IFolderSource> OpenFolderSource,
    Func<Func<EditableAudio, CancellationToken, Task<NameSuggestion>>?> CreateNamer,
    Func<ClipEditorViewModel, IClipEditorWindow> ShowWindow);

/// <summary>
/// Opens pending grabs in clip editor windows, at most one per grab, keeps their folder lists current as the library
/// changes, and applies <see cref="PendingGrabCleanupRule"/> when each closes. UI thread only.
/// </summary>
public sealed class GrabEditorCoordinator
{
    private readonly SettingsMirror _settings;
    private readonly LibraryViewModel _library;
    private readonly StatusViewModel _status;
    private readonly GrabEditorServices _services;
    private readonly Dictionary<string, OpenEditor?> _open = []; // by grab id; null while the grab loads
    private int _folderScan;
    private Task _folderRefresh = Task.CompletedTask;

    public GrabEditorCoordinator(SettingsMirror settings, LibraryViewModel library, StatusViewModel status, GrabEditorServices services)
    {
        _settings = settings;
        _library = library;
        _status = status;
        _services = services;
        _library.ContentsChanged += () => _ = RefreshFoldersAsync();
    }

    /// <summary>Opens the grab in an editor (or brings its window to the front); completes once that editor closes.</summary>
    public async Task EditAsync(PendingGrab grab)
    {
        if (_library.Root is not { } root)
        {
            _status.Report("Choose a sound library folder first.");
            return;
        }

        if (_open.TryGetValue(grab.Id, out var existing))
        {
            if (existing is null)
                _status.Report("Still opening that grab…");
            else
                existing.Window.BringToFront();
            return;
        }

        _open.Add(grab.Id, null);
        try
        {
            await EditAsync(grab, root);
        }
        catch (Exception e) // callers are async void UI handlers
        {
            _status.Report($"The clip editor ran into a problem: {e.Message}");
        }
        finally
        {
            _open.Remove(grab.Id);
        }
    }

    private async Task EditAsync(PendingGrab grab, string root)
    {
        var scan = _folderScan;
        EditableAudio audio;
        IReadOnlyList<LibraryFolder> folders;
        try
        {
            (audio, folders) = await Task.Run(() => (GrabAudio.Load(grab), ScanFolders(root)));
            while (scan != _folderScan) // the library changed while loading; refreshes skip editors not yet shown
            {
                scan = _folderScan;
                folders = await Task.Run(() => ScanFolders(root));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        {
            _status.Report($"Couldn't open the grab: {e.Message}");
            return;
        }

        var editor = new ClipEditorViewModel(audio, _services.Preview, _services.Encoder, new ClipEditorOptions
        {
            LibraryRoot = root,
            Folders = folders,
            RefreshFolders = RefreshFoldersAsync,
            InitialFolder = _library.Folder,
            SuggestName = _services.CreateNamer(),
            PlaySampleOnDrag = _settings.Current.EditorPlaySampleOnDrag,
            PlaySampleOnDragChanged = value => _settings.Update(s => s with { EditorPlaySampleOnDrag = value }),
        });
        editor.ClipSaved += _ => _library.Refresh();
        IClipEditorWindow window;
        try
        {
            window = _services.ShowWindow(editor);
        }
        catch
        {
            editor.Dispose();
            throw;
        }

        _open[grab.Id] = new OpenEditor(editor, window, root);
        var outcome = await window.Closed;
        // Each editor counts only its own saves, so one finishing after this can't leak into the grab's next editor.
        if (PendingGrabCleanupRule.ShouldDelete(outcome == ClipEditorOutcome.Discarded, editor.SavedClipCount))
            Delete(grab);
    }

    private void Delete(PendingGrab grab)
    {
        try
        {
            _services.GrabStore.Delete(grab);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _status.Report($"Couldn't delete the grab: {e.Message}");
        }
    }

    // Every refresh covers all open editors, so a newer one supersedes an older; one superseded waits for its successor.
    private Task RefreshFoldersAsync() => _folderRefresh = RefreshFoldersAsync(++_folderScan);

    private async Task RefreshFoldersAsync(int scan)
    {
        try
        {
            foreach (var root in OpenEditors.Select(e => e.Root).Distinct().ToList())
            {
                var folders = await Task.Run(() => ScanFolders(root));
                if (scan != _folderScan)
                {
                    await _folderRefresh;
                    return;
                }

                foreach (var open in OpenEditors.Where(e => e.Root == root))
                    open.Editor.UpdateFolders(folders);
            }
        }
        catch (Exception e)
        {
            _status.Report($"Couldn't list the library's folders: {e.Message}");
        }
    }

    private IEnumerable<OpenEditor> OpenEditors => _open.Values.OfType<OpenEditor>();

    private IReadOnlyList<LibraryFolder> ScanFolders(string root) => LibraryFolderList.Build(_services.OpenFolderSource(root));

    private sealed record OpenEditor(ClipEditorViewModel Editor, IClipEditorWindow Window, string Root);
}
