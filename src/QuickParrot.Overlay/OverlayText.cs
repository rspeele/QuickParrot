using QuickParrot.Core.Navigation;

namespace QuickParrot.Overlay;

/// <summary>The words the folder layouts show, shared by the grid, ring and list geometry.</summary>
internal static class OverlayText
{
    public const string RootTitle = "QuickParrot";
    public const string EmptyLabel = "(empty)";
    public const string HintKey = "0";
    public const string UpAction = "Up";
    public const string BackAction = "Back";

    public static string TitleFor(string folderPath)
    {
        var separator = folderPath.LastIndexOf('/');
        return folderPath.Length == 0 ? RootTitle : folderPath[(separator + 1)..];
    }

    public static string DisplayName(NumberedEntry entry)
    {
        var name = entry.IsFolder ? entry.Name : Path.GetFileNameWithoutExtension(entry.Name);
        return name.Length > 0 ? name : entry.Name;
    }

    /// <summary>What <see cref="HintKey"/> does here, or null at the top level.</summary>
    public static string? HintActionFor(OverlayViewState state) =>
        state.ZoomedColumn is not null ? BackAction
        : state.FolderPath.Length > 0 ? UpAction
        : null;
}
