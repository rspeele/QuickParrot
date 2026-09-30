using Microsoft.Extensions.Time.Testing;
using QuickParrot.Core.Engine;
using QuickParrot.Core.Mic;
using QuickParrot.Core.Settings;
using QuickParrot.Core.Tests.Fakes;

namespace QuickParrot.Core.Tests.Mic;

public class QuickParrotEngineMicTests
{
    [Fact]
    public async Task Engine_ConfiguresMuterOnStartAndOnSettingsChange()
    {
        List<string> log = [];
        var muter = new FakeMicMuter(log);
        var settings = new AppSettings { MicDuckMode = MicDuckMode.Mute };
        using var engine = new QuickParrotEngine(
            new FakeClipPlayer(log), new FakePushToTalk(log), muter, new FakeSettingsStore(), settings,
            new FakeTimeProvider(), _ => new FakeFolderSource());

        engine.Start();
        await engine.FlushAsync();
        Assert.Equal(new MicDuckSettings(MicDuckMode.Mute, 20, null), muter.Settings);

        engine.UpdateSettings(s => s with { MicDuckMode = MicDuckMode.Attenuate, MicAttenuationPercent = 40 });
        await engine.FlushAsync();
        Assert.Equal(new MicDuckSettings(MicDuckMode.Attenuate, 40, null), muter.Settings);
    }
}
