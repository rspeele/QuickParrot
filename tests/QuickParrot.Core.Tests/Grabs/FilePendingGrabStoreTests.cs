using QuickParrot.Core.Grabs;

namespace QuickParrot.Core.Tests.Grabs;

public sealed class FilePendingGrabStoreTests : IDisposable
{
    private static readonly DateTimeOffset At = new DateTime(2026, 9, 30, 14, 25, 1, DateTimeKind.Local);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "QuickParrotTests", Guid.NewGuid().ToString("N"));
    private readonly FilePendingGrabStore _store;

    public FilePendingGrabStoreTests()
    {
        _store = new FilePendingGrabStore(_directory, maxGrabs: 3);
    }

    [Fact]
    public void List_WhenFolderMissing_IsEmpty() => Assert.Empty(_store.List());

    [Fact]
    public void Save_WritesAWavFile_AndListsIt()
    {
        var changes = 0;
        _store.Changed += () => changes++;

        var grab = _store.Save(new float[2000], 1000, 2, At);

        Assert.Equal("grab-20260930-142501", grab.Id);
        Assert.Equal(Path.Combine(_directory, "grab-20260930-142501.wav"), grab.FilePath);
        Assert.Equal(At, grab.GrabbedAt);
        Assert.Equal(TimeSpan.FromSeconds(1), grab.Duration);
        Assert.Equal([grab], _store.List());
        Assert.Equal(1, changes);
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public void GrabsInTheSameSecond_GetDistinctNames_ListedInOrder()
    {
        var first = _store.Save([0f], 1000, 1, At);
        var second = _store.Save([0f], 1000, 1, At);
        var earlier = _store.Save([0f], 1000, 1, At.AddMinutes(-1));

        Assert.Equal("grab-20260930-142501-2", second.Id);
        Assert.Equal([earlier, first, second], _store.List());
    }

    [Fact]
    public void Save_BeyondTheLimit_DeletesTheOldest()
    {
        var grabs = Enumerable.Range(0, 4).Select(i => _store.Save([0f], 1000, 1, At.AddSeconds(i))).ToList();

        Assert.Equal(grabs[1..], _store.List());
        Assert.False(File.Exists(grabs[0].FilePath));
    }

    [Fact]
    public void Delete_RemovesTheFile_AndToleratesMissingOnes()
    {
        var grab = _store.Save([0f], 1000, 1, At);
        var changes = 0;
        _store.Changed += () => changes++;

        _store.Delete(grab);
        _store.Delete(grab);

        Assert.Empty(_store.List());
        Assert.Equal(2, changes);
    }

    [Fact]
    public void Delete_RefusesFilesOutsideTheStore()
    {
        var outside = new PendingGrab("x", Path.Combine(Path.GetTempPath(), "grab-20260930-142501.wav"), At, TimeSpan.Zero);

        Assert.Throws<ArgumentException>(() => _store.Delete(outside));
    }

    [Fact]
    public void LeftoverTempFiles_AreCleanedUp_AndOtherFilesIgnored()
    {
        Directory.CreateDirectory(_directory);
        var temp = Path.Combine(_directory, "grab-20260930-142501.tmp");
        File.WriteAllText(temp, "partial");
        File.WriteAllText(Path.Combine(_directory, "grab-20260930-142502.wav"), "not a wav");
        File.WriteAllText(Path.Combine(_directory, "notes.wav"), "");

        Assert.Empty(new FilePendingGrabStore(_directory).List());
        Assert.False(File.Exists(temp));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }
}
