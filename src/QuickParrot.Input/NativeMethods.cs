using System.Runtime.InteropServices;

namespace QuickParrot.Input;

internal static unsafe partial class NativeMethods
{
    public const int WH_KEYBOARD_LL = 13;
    public const int HC_ACTION = 0;

    public const uint LLKHF_EXTENDED = 0x01;
    public const uint LLKHF_LOWER_IL_INJECTED = 0x02;
    public const uint LLKHF_INJECTED = 0x10;
    public const uint LLKHF_UP = 0x80;

    public const uint WM_QUIT = 0x0012;
    public const uint WM_TIMER = 0x0113;
    public const uint WM_WTSSESSION_CHANGE = 0x02B1;
    public const uint WM_APP = 0x8000;

    public const int WTS_CONSOLE_CONNECT = 0x1;
    public const int WTS_CONSOLE_DISCONNECT = 0x2;
    public const int WTS_REMOTE_CONNECT = 0x3;
    public const int WTS_REMOTE_DISCONNECT = 0x4;
    public const int WTS_SESSION_LOCK = 0x7;
    public const int WTS_SESSION_UNLOCK = 0x8;
    public const uint NOTIFY_FOR_THIS_SESSION = 0;

    public const int ERROR_CLASS_ALREADY_EXISTS = 1410;

    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    public const uint EVENT_SYSTEM_DESKTOPSWITCH = 0x0020;
    public const uint WINEVENT_OUTOFCONTEXT = 0;

    public const uint MAPVK_VSC_TO_VK_EX = 3;

    public static readonly nint HWND_MESSAGE = -3;

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public int ptX;
        public int ptY;
        public uint lPrivate;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public delegate* unmanaged<nint, uint, nint, nint, nint> lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public char* lpszMenuName;
        public char* lpszClassName;
        public nint hIconSm;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial nint SetWindowsHookExW(
        int idHook, delegate* unmanaged<int, nint, nint, nint> lpfn, nint hmod, uint dwThreadId);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnhookWindowsHookEx(nint hhk);

    [LibraryImport("user32.dll")]
    public static partial nint SetWinEventHook(
        uint eventMin, uint eventMax, nint hmodWinEventProc,
        delegate* unmanaged<nint, uint, nint, int, int, uint, uint, void> pfnWinEventProc,
        uint idProcess, uint idThread, uint dwFlags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnhookWinEvent(nint hWinEventHook);

    [LibraryImport("user32.dll")]
    public static partial nuint SetTimer(nint hWnd, nuint nIDEvent, uint uElapse, nint lpTimerFunc);

    [LibraryImport("user32.dll")]
    public static partial short GetAsyncKeyState(int vKey);

    [LibraryImport("user32.dll")]
    public static partial uint MapVirtualKeyExW(uint uCode, uint uMapType, nint dwhkl);

    [LibraryImport("user32.dll")]
    public static partial nint GetKeyboardLayout(uint idThread);

    [LibraryImport("user32.dll")]
    public static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    public static partial uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [LibraryImport("user32.dll")]
    public static partial nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial int GetMessageW(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [LibraryImport("user32.dll")]
    public static partial nint DispatchMessageW(in MSG lpMsg);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostThreadMessageW(uint idThread, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial ushort RegisterClassExW(in WNDCLASSEXW lpwcx);

    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateWindowExW(
        uint dwExStyle, string lpClassName, string? lpWindowName, uint dwStyle, int x, int y, int nWidth, int nHeight,
        nint hWndParent, nint hMenu, nint hInstance, nint lpParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    public static partial nint DefWindowProcW(nint hWnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint GetModuleHandleW(string? lpModuleName);

    [LibraryImport("kernel32.dll")]
    public static partial uint GetCurrentThreadId();

    [LibraryImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WTSRegisterSessionNotification(nint hWnd, uint dwFlags);

    [LibraryImport("wtsapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WTSUnRegisterSessionNotification(nint hWnd);
}
