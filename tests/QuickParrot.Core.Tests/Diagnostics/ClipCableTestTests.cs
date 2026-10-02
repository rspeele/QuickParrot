using QuickParrot.Core.Diagnostics;

namespace QuickParrot.Core.Tests.Diagnostics;

public sealed class ClipCableTestTests : IDisposable
{
    private static readonly TimeSpan PreRoll = TimeSpan.FromSeconds(0.5);
    private static readonly TimeSpan PostRoll = TimeSpan.FromSeconds(0.5);
    private static readonly TimeSpan Length = ClipCableTest.RecordingLength(TimeSpan.FromSeconds(1), PreRoll, PostRoll);

    private readonly List<string> _log = [];
    private readonly FakeAudio _audio;
    private readonly CancellationTokenSource _cts = new();
    private Action<int>? _onDelay;

    public ClipCableTestTests() => _audio = new FakeAudio(_log);

    public void Dispose() => _cts.Dispose();

    [Fact]
    public async Task Run_PlaysWhileRecording_AndStopsAfterTheRecording()
    {
        var result = await RunAsync();

        Assert.Equal(
            [
                "load:C:/lib/wall.wav",
                $"record:capture:{(Length + TimeSpan.FromSeconds(1)).TotalSeconds}",
                $"delay:{ClipCableTest.Lead.TotalSeconds}",
                "play",
                $"delay:{(Length - ClipCableTest.Lead).TotalSeconds}",
                "record:stop",
                "stop",
                "record:dispose",
            ],
            _log);
        Assert.Null(result.Error);
        Assert.Same(_audio.Recorded, result.Recording);
        Assert.NotNull(result.Analysis);
    }

    [Fact]
    public void RecordingLength_CoversTheClipAndItsMargins_UpToTheCap()
    {
        Assert.Equal(
            ClipCableTest.Lead + PreRoll + TimeSpan.FromSeconds(2) + PostRoll + ClipCableTest.Slack,
            ClipCableTest.RecordingLength(TimeSpan.FromSeconds(2), PreRoll, PostRoll));
        Assert.Equal(ClipCableTest.MaxRecording, ClipCableTest.RecordingLength(TimeSpan.FromMinutes(2), PreRoll, PostRoll));
    }

    [Fact]
    public void SearchFrom_StartsJustBeforeThePreRollEnds_NeverBeforeTheRecording()
    {
        Assert.Equal(TimeSpan.FromSeconds(0.7), ClipCableTest.SearchFrom(PreRoll));
        Assert.Equal(TimeSpan.FromSeconds(0.2), ClipCableTest.SearchFrom(TimeSpan.Zero));
    }

    [Fact]
    public async Task CancelWhileRecording_StopsPlaybackAndRecording_AndThrows()
    {
        _onDelay = call =>
        {
            if (call == 2)
                _cts.Cancel();
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(RunAsync);

        Assert.Equal(["play", $"delay:{(Length - ClipCableTest.Lead).TotalSeconds}", "stop", "record:dispose"], _log[^4..]);
    }

    [Fact]
    public async Task CancelBeforePlaying_NeverPlaysOrStops()
    {
        _onDelay = _ => _cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(RunAsync);

        Assert.DoesNotContain("play", _log);
        Assert.DoesNotContain("stop", _log);
        Assert.Contains("record:dispose", _log);
    }

    [Fact]
    public async Task UnreadableClip_FailsWithoutRecording()
    {
        _audio.LoadError = new InvalidDataException("The file isn't a supported audio format.");

        var result = await RunAsync();

        Assert.Equal("Couldn't read wall: The file isn't a supported audio format.", result.Error);
        Assert.Equal(["load:C:/lib/wall.wav"], _log);
    }

    [Fact]
    public async Task CableThatWontRecord_Fails()
    {
        _audio.StartError = new InvalidOperationException("Device in use.");

        var result = await RunAsync();

        Assert.Equal("Couldn't record from CABLE Output: Device in use.", result.Error);
        Assert.DoesNotContain("play", _log);
    }

    [Fact]
    public async Task RecordingThatFails_StillStopsTheClip()
    {
        _audio.StopError = new InvalidOperationException("Unsupported format.");

        var result = await RunAsync();

        Assert.Equal("Recording from CABLE Output failed: Unsupported format.", result.Error);
        Assert.Equal(["stop", "record:dispose"], _log[^2..]);
    }

    [Fact]
    public async Task UnexpectedFailure_BecomesAFailedResult()
    {
        var test = new ClipCableTest(_audio, (_, _) => Task.CompletedTask);
        var request = new ClipCableTestRequest("wall", "C:/lib/wall.wav", "capture", PreRoll, PostRoll);

        var result = await test.RunAsync(request, () => { }, () => { }, new ThrowingProgress(), CancellationToken.None);

        Assert.Equal("The test failed: Progress broke.", result.Error);
    }

    [Fact]
    public async Task EmptyRecording_SaysTheCableDeliveredNothing()
    {
        _audio.Recorded = new MonoAudio([], 48000);

        var result = await RunAsync();

        Assert.Equal(CableTestMessages.NoAudioFromCable, result.Error);
    }

    [Fact]
    public async Task PlayBack_PlaysOnTheMonitor_OrSaysWhyNot()
    {
        var test = new ClipCableTest(_audio, (_, _) => Task.CompletedTask);

        Assert.Null(await test.PlayBackAsync("monitor", _audio.Recorded, CancellationToken.None));
        Assert.Equal(CableTestMessages.NoMonitor, await test.PlayBackAsync(null, _audio.Recorded, CancellationToken.None));
        Assert.Equal(["playback:monitor"], _log);

        _audio.PlayError = "Headphones unplugged.";
        Assert.Equal("Headphones unplugged.", await test.PlayBackAsync("monitor", _audio.Recorded, CancellationToken.None));
    }

    [Fact]
    public async Task PlayBack_ThatThrows_ReportsTheError()
    {
        _audio.PlayThrows = new InvalidOperationException("Device gone.");
        var test = new ClipCableTest(_audio, (_, _) => Task.CompletedTask);

        Assert.Equal("Device gone.", await test.PlayBackAsync("monitor", _audio.Recorded, CancellationToken.None));
    }

    [Fact]
    public void Describe_ListsTheAnalysisThenTheGainThenAnyPlaybackError()
    {
        var result = new ClipCableTestResult(
            Analysis: new ClipLevelAnalysis(ClipLevelVerdict.NoDrop, -3), PlaybackError: "Headphones unplugged.");

        var lines = result.Describe();

        Assert.Equal(3, lines.Count);
        Assert.Equal("No level drop detected.", lines[0]);
        Assert.Contains("3 dB quieter than the file", lines[1]);
        Assert.Equal("Couldn't play the recording back: Headphones unplugged.", lines[2]);
        Assert.Equal(["Oops"], ClipCableTestResult.Failed("Oops").Describe());
    }

    private Task<ClipCableTestResult> RunAsync()
    {
        var calls = 0;
        var test = new ClipCableTest(_audio, (delay, token) =>
        {
            _log.Add($"delay:{delay.TotalSeconds}");
            _onDelay?.Invoke(++calls);
            token.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        });
        var request = new ClipCableTestRequest("wall", "C:/lib/wall.wav", "capture", PreRoll, PostRoll);
        return test.RunAsync(request, () => _log.Add("play"), () => _log.Add("stop"), null, _cts.Token);
    }

    private sealed class ThrowingProgress : IProgress<string>
    {
        public void Report(string value) => throw new InvalidOperationException("Progress broke.");
    }

    private sealed class FakeAudio(List<string> log) : IClipCableAudio
    {
        public Exception? LoadError { get; set; }

        public Exception? StartError { get; set; }

        public Exception? StopError { get; set; }

        public string? PlayError { get; set; }

        public Exception? PlayThrows { get; set; }

        public MonoAudio Clip { get; } = new(new float[48000], 48000);

        public MonoAudio Recorded { get; set; } = new(new float[48000 * 3], 48000);

        public Task<MonoAudio> LoadClipAsync(string fullPath, CancellationToken cancellationToken)
        {
            log.Add($"load:{fullPath}");
            return LoadError is null ? Task.FromResult(Clip) : Task.FromException<MonoAudio>(LoadError);
        }

        public ICableRecording StartRecording(string captureDeviceId, TimeSpan maxDuration)
        {
            if (StartError is not null)
                throw StartError;

            log.Add($"record:{captureDeviceId}:{maxDuration.TotalSeconds}");
            return new FakeRecording(this, log);
        }

        public Task<string?> PlayAsync(string renderDeviceId, MonoAudio audio, CancellationToken cancellationToken)
        {
            if (PlayThrows is not null)
                throw PlayThrows;

            log.Add($"playback:{renderDeviceId}");
            return Task.FromResult(PlayError);
        }

        private sealed class FakeRecording(FakeAudio audio, List<string> log) : ICableRecording
        {
            public Task<MonoAudio> StopAsync()
            {
                log.Add("record:stop");
                return audio.StopError is null ? Task.FromResult(audio.Recorded) : Task.FromException<MonoAudio>(audio.StopError);
            }

            public void Dispose() => log.Add("record:dispose");
        }
    }
}
