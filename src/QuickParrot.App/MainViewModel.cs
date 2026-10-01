namespace QuickParrot.App;

/// <summary>The main window: hosts each tab's view model and the grab editors.</summary>
public sealed class MainViewModel
{
    public MainViewModel(
        StatusViewModel status,
        LibraryViewModel library,
        PendingGrabsViewModel pendingGrabs,
        FavoritesViewModel favorites,
        SettingsViewModel general,
        DeviceSettingsViewModel devices,
        HotkeysViewModel hotkeys,
        LiteLlmSettingsViewModel liteLlm,
        DiagnosticsViewModel diagnostics,
        GrabEditorCoordinator editors)
    {
        Status = status;
        Library = library;
        PendingGrabs = pendingGrabs;
        Favorites = favorites;
        Settings = general;
        Devices = devices;
        Hotkeys = hotkeys;
        LiteLlm = liteLlm;
        Diagnostics = diagnostics;
        Editors = editors;
        Library.ContentsChanged += Favorites.Refresh; // files changed on disk, so the missing marks may be stale
    }

    public StatusViewModel Status { get; }

    public LibraryViewModel Library { get; }

    public PendingGrabsViewModel PendingGrabs { get; }

    public FavoritesViewModel Favorites { get; }

    public SettingsViewModel Settings { get; }

    public DeviceSettingsViewModel Devices { get; }

    public HotkeysViewModel Hotkeys { get; }

    public LiteLlmSettingsViewModel LiteLlm { get; }

    public DiagnosticsViewModel Diagnostics { get; }

    public GrabEditorCoordinator Editors { get; }
}
