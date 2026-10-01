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
    private readonly SettingsMirror _settings;
    private readonly StatusViewModel _status;
    private IReadOnlyList<PendingGrabItem> _grabs = [];

    public PendingGrabsViewModel(
        IPendingGrabStore store, QuickParrotEngine engine, SettingsMirror settings, StatusViewModel status, Action<Action> postToUi)
    {
        _store = store;
        _engine = engine;
        _settings = settings;
        _status = status;
        _store.Changed += () => postToUi(Refresh);
        _settings.Changed += (old, now) =>
        {
            if (BufferStatusOf(old) != BufferStatusOf(now))
                OnPropertyChanged(nameof(BufferStatus));
        };
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

    public string BufferStatus => BufferStatusOf(_settings.Current);

    public void GrabNow() => _engine.Grab();

    /// <summary>Reports failure on the status line, e.g. when another program has the file open.</summary>
    public void Delete(PendingGrabItem? item)
    {
        if (item is null)
            return;

        try
        {
            _store.Delete(item.Grab);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _status.Report($"Couldn't delete the grab: {e.Message}");
        }
    }

    /// <summary>Oldest-first from the store, newest-first for display.</summary>
    private void Refresh() => Grabs = _store.List().Reverse().Select(g => new PendingGrabItem(g)).ToList();

    private static string BufferStatusOf(AppSettings settings) => settings.ReplayBufferEnabled
        ? $"Replay buffer: on ({settings.ReplayBufferSeconds} s)"
        : "Replay buffer: off";
}
