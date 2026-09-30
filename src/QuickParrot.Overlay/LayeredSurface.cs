using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using static QuickParrot.Overlay.NativeMethods;

namespace QuickParrot.Overlay;

/// <summary>
/// A top-down 32bpp DIB that GDI+ draws into directly as premultiplied ARGB, which is exactly the format
/// UpdateLayeredWindow wants for per-pixel alpha.
/// </summary>
internal sealed class LayeredSurface : IDisposable
{
    private readonly nint _dc;
    private readonly nint _bitmap;
    private readonly nint _previous;
    private readonly nint _bits;

    public LayeredSurface(Size size)
    {
        Size = size;
        var header = new BITMAPINFOHEADER
        {
            Size = Marshal.SizeOf<BITMAPINFOHEADER>(),
            Width = size.Width,
            Height = -size.Height, // negative = top-down, matching GDI+ scan order
            Planes = 1,
            BitCount = 32,
        };

        _dc = CreateCompatibleDC(0);
        _bitmap = CreateDIBSection(_dc, header, DIB_RGB_COLORS, out _bits, 0, 0);
        if (_dc == 0 || _bitmap == 0)
        {
            Dispose();
            throw new Win32Exception("Couldn't create the overlay surface.");
        }

        _previous = SelectObject(_dc, _bitmap);
    }

    public Size Size { get; }

    /// <summary>Clears to fully transparent and wraps the pixels for drawing; dispose it before presenting.</summary>
    public unsafe Bitmap BeginDraw()
    {
        NativeMemory.Clear((void*)_bits, (nuint)(Size.Width * Size.Height * 4));
        return new Bitmap(Size.Width, Size.Height, Size.Width * 4, PixelFormat.Format32bppPArgb, _bits);
    }

    public void Present(nint hwnd, Point topLeft)
    {
        var blend = new BLENDFUNCTION { BlendOp = AC_SRC_OVER, SourceConstantAlpha = 255, AlphaFormat = AC_SRC_ALPHA };
        var dst = new POINT { X = topLeft.X, Y = topLeft.Y };
        var size = new SIZE { Width = Size.Width, Height = Size.Height };
        if (!UpdateLayeredWindow(hwnd, 0, dst, size, _dc, default, 0, blend, ULW_ALPHA))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
    }

    public void Dispose()
    {
        if (_dc != 0 && _previous != 0)
            SelectObject(_dc, _previous);

        if (_bitmap != 0)
            DeleteObject(_bitmap);

        if (_dc != 0)
            DeleteDC(_dc);
    }
}
