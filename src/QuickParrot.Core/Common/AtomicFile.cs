namespace QuickParrot.Core.Common;

public static class AtomicFile
{
    /// <summary>Writes a temp file and moves it into place, so a crash mid-write can't leave a truncated file.</summary>
    public static void WriteAllText(string path, string contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, contents);
        File.Move(tempPath, path, overwrite: true);
    }
}
