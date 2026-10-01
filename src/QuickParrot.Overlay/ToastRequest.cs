using QuickParrot.Core.Navigation;

namespace QuickParrot.Overlay;

/// <summary>A toast to show until <see cref="ExpiresAt"/>, in <see cref="Environment.TickCount64"/> milliseconds.</summary>
internal sealed record ToastRequest(string Text, bool IsError, long ExpiresAt)
{
    public static ToastRequest Create(string text, bool isError, TimeSpan duration, long now) =>
        new(text, isError, now + (long)duration.TotalMilliseconds);

    public bool IsLive(long now) => now < ExpiresAt;

    /// <summary>What to draw: a chord view wins, and a live toast waits underneath it.</summary>
    public static object? ChooseScene(OverlayViewState? state, ToastRequest? toast, long now) =>
        (object?)state ?? (toast is not null && toast.IsLive(now) ? toast : null);
}
