using System.IO;
using QuickParrot.App.Mvvm;
using QuickParrot.Core.Engine;
using QuickParrot.Core.Grabs;
using QuickParrot.Core.Settings;

namespace QuickParrot.App;

public sealed record PendingGrabItem(PendingGrab Grab)
{
    public string Display => PendingGrabDisplay.Format(Grab);
}

/// <summary>
/// The Library tab's pending-grabs list: newest first, kept in sync with the store's <see cref="IPendingGrabStore.Changed"/>
/// event. Opening a grab is the window's job, since that needs editor services this view model doesn't own.
/// </summary>
public sealed class PendingGrabsViewModel : ObservableObject
{
    private readonly IPendingGrabStore _store;
    private readonly QuickParrotEngine _engine;
    private readonly SynchronizationContext _ui;
    private IReadOnlyList<PendingGrabItem> _grabs = [];
    private string _bufferStatus = "";

    public PendingGrabsViewModel(IPendingGrabStore store, QuickParrotEngine engine)
    {
        _store = store;
        _engine = engine;
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();
        _store.Changed += () => _ui.Post(_ => Refresh(), null);
        _engine.SettingsChanged += settings => _ui.Post(_ => UpdateBufferStatus(settings), null);
        UpdateBufferStatus(engine.Settings);
        Refresh();
    }

    public IReadOnlyList<PendingGrabItem> Grabs
    {
        get => _grabs;
        private set
        {
            if (SetField(ref _grabs, value))
                OnPropertyChanged(nameof(Count));
        }
    }

    public int Count => _grabs.Count;

    public string BufferStatus
    {
        get => _bufferStatus;
        private set => SetField(ref _bufferStatus, value);
    }

    public void GrabNow() => _engine.Grab();

    /// <summary>Returns a user-facing error, e.g. when another program has the file open.</summary>
    public string? Delete(PendingGrabItem? item)
    {
        if (item is null)
            return null;

        try
        {
            _store.Delete(item.Grab);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return $"Couldn't delete the grab: {e.Message}";
        }
    }

    /// <summary>Oldest-first from the store, newest-first for display.</summary>
    private void Refresh() => Grabs = _store.List().Reverse().Select(g => new PendingGrabItem(g)).ToList();

    private void UpdateBufferStatus(AppSettings settings) => BufferStatus = settings.ReplayBufferEnabled
        ? $"Replay buffer: on ({settings.ReplayBufferSeconds} s)"
        : "Replay buffer: off";
}
