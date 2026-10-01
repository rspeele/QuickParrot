namespace QuickParrot.App.Editor;

/// <summary>A shown clip editor window, as its opener sees it.</summary>
public interface IClipEditorWindow
{
    /// <summary>Completes once the window has closed, with how it was closed.</summary>
    Task<ClipEditorOutcome> Closed { get; }

    void BringToFront();
}
