using Microsoft.Extensions.Time.Testing;
using QuickParrot.Core.Playback;
using QuickParrot.Core.Tests.Fakes;

namespace QuickParrot.Core.Tests.Playback;

public abstract class PlaybackControllerFixture : IDisposable
{
    protected const string ClipA = @"C:\lib\a.wav";
    protected const string ClipB = @"C:\lib\b.wav";
    protected const string ClipC = @"C:\lib\c.wav";

    protected PlaybackControllerFixture()
    {
        Player = new FakeClipPlayer(Log);
        PushToTalk = new FakePushToTalk(Log);
    }

    protected List<string> Log { get; } = [];

    protected FakeTimeProvider Time { get; } = new();

    protected FakeClipPlayer Player { get; }

    protected FakePushToTalk PushToTalk { get; }

    protected List<PlaybackError> Errors { get; } = [];

    /// <summary>Exceptions thrown by dispatched callbacks, which a prepare's continuation would otherwise swallow.</summary>
    protected List<Exception> DispatchErrors { get; } = [];

    protected PlaybackController Create(PlaybackOptions? options = null, Action<Action>? dispatch = null) =>
        new(Player, PushToTalk, new FakeMicMuter(Log), Time, options ?? PlaybackOptions.Default, Errors.Add,
            dispatch ?? RunInline);

    protected void Advance(int milliseconds) => Time.Advance(TimeSpan.FromMilliseconds(milliseconds));

    /// <summary>Starts <paramref name="path"/> from idle and runs out the pre-roll, then clears the log.</summary>
    protected void PlayThroughPreRoll(PlaybackController controller, string path)
    {
        controller.Play(path);
        Time.Advance(controller.Options.PreRoll);
        Assert.Equal(PlaybackPhase.Playing, controller.Phase);
        Log.Clear();
    }

    public void Dispose()
    {
        Assert.Empty(DispatchErrors);
        GC.SuppressFinalize(this);
    }

    protected static string Prepared(string path) => $"prepare:{path}";

    protected static string Play(string path) => $"play:{path}";

    private void RunInline(Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            DispatchErrors.Add(e);
            throw;
        }
    }
}
