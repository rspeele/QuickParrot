
namespace QuickParrot.Core.Keyboard;

/// <summary>What the app needs from the keyboard hook, so a view model can be built without a real one.</summary>
public interface IChordKeyHook
{
    ScanKey ChordKey { get; set; }

    bool Enabled { get; set; }

    /// <summary>Settable from any thread; moves an in-progress hold across to the new binding.</summary>
    PushToTalkBinding PushToTalkBinding { get; set; }

    Task<ScanKey?> CaptureNextKeyAsync(CancellationToken cancellationToken = default);
}
