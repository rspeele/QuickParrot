namespace QuickParrot.Core.Common;

/// <summary>Where the app keeps its own files, in a QuickParrot folder under the user's app data.</summary>
public static class AppDataPaths
{
    public static string Roaming(string name) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QuickParrot", name);

    public static string Local(string name) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QuickParrot", name);
}
