using static QuickParrot.Input.NativeMethods;

namespace QuickParrot.Input;

internal sealed unsafe class KeyboardTextTranslator
{
    private readonly byte[] _state = new byte[256];
    private nint _layout;
    private bool _deadKey;

    public void Reset()
    {
        ClearDeadKey();
        for (var key = 0; key < _state.Length; key++)
            _state[key] = (byte)(((GetAsyncKeyState(key) & 0x8000) != 0 ? 0x80 : 0) | (GetKeyState(key) & 1));
    }

    public void Update(uint virtualKey, bool down)
    {
        if (virtualKey >= _state.Length)
            return;
        var key = (int)virtualKey;
        var wasDown = (_state[key] & 0x80) != 0;
        if (down && !wasDown && key is 0x14 or 0x90 or 0x91)
            _state[key] ^= 1;
        _state[key] = (byte)((_state[key] & 1) | (down ? 0x80 : 0));
        _state[0x10] = (byte)((_state[0xA0] | _state[0xA1]) & 0x80);
        _state[0x11] = (byte)((_state[0xA2] | _state[0xA3]) & 0x80);
        _state[0x12] = (byte)((_state[0xA4] | _state[0xA5]) & 0x80);
    }

    public string Translate(uint virtualKey, uint scanCode)
    {
        var layout = GetKeyboardLayout(GetWindowThreadProcessId(GetForegroundWindow(), out _));
        if (layout != _layout)
        {
            ClearDeadKey();
            _layout = layout;
        }
        Span<char> text = stackalloc char[16];
        fixed (byte* state = _state)
        fixed (char* output = text)
        {
            var count = ToUnicodeEx(virtualKey, scanCode, state, output, text.Length, 0, _layout);
            if (count < 0)
            {
                _deadKey = true;
                return "";
            }
            if (count > 0)
                _deadKey = false;
            return count > 0 ? new string(text[..Math.Min(count, text.Length)]) : "";
        }
    }

    public void ClearDeadKey()
    {
        if (!_deadKey)
            return;
        Span<byte> state = stackalloc byte[256];
        state.Clear();
        Span<char> text = stackalloc char[16];
        fixed (byte* keyboard = state)
        fixed (char* output = text)
        {
            for (var attempt = 0; attempt < 4 && ToUnicodeEx(0x20, 0x39, keyboard, output, text.Length, 0, _layout) < 0; attempt++)
            {
            }
        }
        _deadKey = false;
    }
}
