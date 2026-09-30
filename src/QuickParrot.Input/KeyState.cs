using QuickParrot.Core.Keyboard;
using QuickParrot.Core.Playback;
using static QuickParrot.Input.NativeMethods;

namespace QuickParrot.Input;

/// <summary>Whether other apps see a key or button down; presses a hook hid never show up here.</summary>
internal static class KeyState
{
    public static bool IsDown(ScanKey key)
    {
        var layout = GetKeyboardLayout(GetWindowThreadProcessId(GetForegroundWindow(), out _));
        var scanCode = (uint)(key.IsExtended ? 0xE000 | key.ScanCode : key.ScanCode);
        var vk = MapVirtualKeyExW(scanCode, MAPVK_VSC_TO_VK_EX, layout);
        return vk != 0 && GetAsyncKeyState((int)vk) < 0;
    }

    public static bool IsDown(PushToTalkBinding binding)
    {
        if (binding.Key is { } key)
            return IsDown(key);

        var vk = binding.MouseButton switch
        {
            PushToTalkMouseButton.Middle => VK_MBUTTON,
            PushToTalkMouseButton.X1 => VK_XBUTTON1,
            PushToTalkMouseButton.X2 => VK_XBUTTON2,
            _ => 0,
        };
        return vk != 0 && GetAsyncKeyState(vk) < 0;
    }
}
