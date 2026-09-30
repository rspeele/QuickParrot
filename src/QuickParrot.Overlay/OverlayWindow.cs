using System.Drawing;
using System.Windows.Forms;
using static QuickParrot.Overlay.NativeMethods;

namespace QuickParrot.Overlay;

/// <summary>A click-through, never-activated, topmost layered popup with no taskbar or Alt-Tab presence.</summary>
internal sealed class OverlayWindow : NativeWindow
{
    public const int WM_APPLY = WM_APP + 1;
    public const int WM_QUIT_LOOP = WM_APP + 2;

    public const int ExtendedStyles =
        WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;

    private readonly Action<int> _onAppMessage;

    public OverlayWindow(Action<int> onAppMessage)
    {
        _onAppMessage = onAppMessage;
        CreateHandle(new CreateParams
        {
            Caption = "QuickParrot overlay",
            Style = WS_POPUP,
            ExStyle = ExtendedStyles,
        });
    }

    public bool IsShown { get; private set; }

    public void ShowAt(LayeredSurface surface, Point topLeft)
    {
        surface.Present(Handle, topLeft);
        if (!IsShown)
        {
            ShowWindow(Handle, SW_SHOWNOACTIVATE);
            IsShown = true;
        }

        // Games can be topmost too, so re-assert our place above them.
        SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
    }

    public void Hide()
    {
        if (!IsShown)
            return;

        ShowWindow(Handle, SW_HIDE);
        IsShown = false;
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WM_MOUSEACTIVATE:
                m.Result = MA_NOACTIVATE;
                return;
            case WM_NCHITTEST:
                m.Result = HTTRANSPARENT;
                return;
            case WM_APPLY or WM_QUIT_LOOP:
                _onAppMessage(m.Msg);
                return;
            default:
                base.WndProc(ref m);
                return;
        }
    }
}
