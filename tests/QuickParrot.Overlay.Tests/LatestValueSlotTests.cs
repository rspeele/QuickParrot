namespace QuickParrot.Overlay.Tests;

public sealed class LatestValueSlotTests
{
    [Fact]
    public void OnlyTheFirstSetOfABatch_AsksForAWakeUp()
    {
        var slot = new LatestValueSlot<string>();

        Assert.True(slot.Set("a"));
        Assert.False(slot.Set("b"));
        Assert.False(slot.Set(null));
        Assert.Null(slot.Take());
        Assert.True(slot.Set("c"));
        Assert.Equal("c", slot.Take());
    }

    [Fact]
    public void CancelledWake_LetsTheNextSetAskAgain()
    {
        var slot = new LatestValueSlot<string>();
        slot.Set("a");

        slot.CancelWake();

        Assert.True(slot.Set("b"));
    }

    [Fact]
    public void RequestWake_KeepsTheValue()
    {
        var slot = new LatestValueSlot<string>();
        slot.Set("a");
        slot.CancelWake();

        Assert.True(slot.RequestWake());
        Assert.False(slot.RequestWake());
        Assert.Equal("a", slot.Take());
    }

    [Fact]
    public void ConcurrentProducer_ConsumerAlwaysEndsOnTheLastValue()
    {
        var slot = new LatestValueSlot<string>();
        var values = Enumerable.Range(0, 20_000).Select(i => i.ToString()).ToArray();
        using var wake = new SemaphoreSlim(0);
        var taken = 0;

        var producer = Task.Run(() =>
        {
            foreach (var value in values)
            {
                if (slot.Set(value))
                    wake.Release();
            }
        });

        string? last = null;
        while (!producer.IsCompleted || wake.CurrentCount > 0)
        {
            if (wake.Wait(10))
            {
                last = slot.Take();
                taken++;
            }
        }

        Assert.Equal(values[^1], last);
        Assert.InRange(taken, 1, values.Length);
    }
}
