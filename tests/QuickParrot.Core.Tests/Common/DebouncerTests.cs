using Microsoft.Extensions.Time.Testing;
using QuickParrot.Core.Common;

namespace QuickParrot.Core.Tests.Common;

public class DebouncerTests
{
    [Fact]
    public void OneSignal_FiresAfterTheDelay()
    {
        var time = new FakeTimeProvider();
        var fires = 0;
        using var debouncer = new Debouncer(() => fires++, TimeSpan.FromMilliseconds(300), time);

        debouncer.Signal();
        time.Advance(TimeSpan.FromMilliseconds(299));
        Assert.Equal(0, fires);

        time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(1, fires);
    }

    [Fact]
    public void ABurstOfSignals_FiresOnlyOnce_AfterTheLastOneSettles()
    {
        var time = new FakeTimeProvider();
        var fires = 0;
        using var debouncer = new Debouncer(() => fires++, TimeSpan.FromMilliseconds(300), time);

        debouncer.Signal();
        time.Advance(TimeSpan.FromMilliseconds(200));
        debouncer.Signal();
        time.Advance(TimeSpan.FromMilliseconds(200));
        debouncer.Signal();
        time.Advance(TimeSpan.FromMilliseconds(299));
        Assert.Equal(0, fires);

        time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(1, fires);
    }

    [Fact]
    public void SecondSettledPeriod_FiresAgain()
    {
        var time = new FakeTimeProvider();
        var fires = 0;
        using var debouncer = new Debouncer(() => fires++, TimeSpan.FromMilliseconds(300), time);

        debouncer.Signal();
        time.Advance(TimeSpan.FromMilliseconds(300));
        Assert.Equal(1, fires);

        debouncer.Signal();
        time.Advance(TimeSpan.FromMilliseconds(300));
        Assert.Equal(2, fires);
    }

    [Fact]
    public void Dispose_CancelsAPendingSignal()
    {
        var time = new FakeTimeProvider();
        var fires = 0;
        var debouncer = new Debouncer(() => fires++, TimeSpan.FromMilliseconds(300), time);

        debouncer.Signal();
        debouncer.Dispose();
        time.Advance(TimeSpan.FromMilliseconds(300));

        Assert.Equal(0, fires);
    }
}
