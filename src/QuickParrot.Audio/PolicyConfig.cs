using System.Runtime.InteropServices;

namespace QuickParrot.Audio;

internal enum EndpointRole
{
    Console = 0,
    Multimedia = 1,
    Communications = 2,
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativePropertyKey(Guid formatId, int propertyId)
{
    public Guid FormatId = formatId;
    public int PropertyId = propertyId;
}

// PROPVARIANT, covering just the VT_BOOL and VT_LPWSTR cases used here; sized for x64 (24 bytes).
[StructLayout(LayoutKind.Explicit)]
internal struct NativePropVariant
{
    public const ushort VtEmpty = 0;
    public const ushort VtBool = 11;
    public const ushort VtLpwstr = 31;

    [FieldOffset(0)] public ushort VarType;
    [FieldOffset(8)] public short BoolValue;
    [FieldOffset(8)] public IntPtr Pointer;
    [FieldOffset(16)] private IntPtr _padding;

    public static NativePropVariant FromBool(bool value) => new() { VarType = VtBool, BoolValue = (short)(value ? -1 : 0) };

    /// <summary>The caller must free <see cref="Pointer"/> with <see cref="Marshal.FreeCoTaskMem"/>.</summary>
    public static NativePropVariant FromString(string value) =>
        new() { VarType = VtLpwstr, Pointer = Marshal.StringToCoTaskMemUni(value) };

    public object? ToObject() => VarType switch
    {
        VtBool => BoolValue != 0,
        VtLpwstr => Marshal.PtrToStringUni(Pointer),
        _ => null,
    };

    [DllImport("ole32.dll")]
    public static extern int PropVariantClear(ref NativePropVariant value);
}

// Undocumented Windows 7+ interface, laid out as in EarTrumpet, SoundSwitch and AudioDeviceCmdlets. Methods this
// app never calls take IntPtr so nothing is marshalled for them; only their vtable slots matter.
[ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfig
{
    [PreserveSig] int GetMixFormat(IntPtr deviceId, IntPtr format);

    [PreserveSig] int GetDeviceFormat(IntPtr deviceId, int useDefault, IntPtr format);

    [PreserveSig] int ResetDeviceFormat(IntPtr deviceId);

    [PreserveSig] int SetDeviceFormat(IntPtr deviceId, IntPtr endpointFormat, IntPtr mixFormat);

    [PreserveSig] int GetProcessingPeriod(IntPtr deviceId, int useDefault, IntPtr defaultPeriod, IntPtr minimumPeriod);

    [PreserveSig] int SetProcessingPeriod(IntPtr deviceId, IntPtr period);

    [PreserveSig] int GetShareMode(IntPtr deviceId, IntPtr mode);

    [PreserveSig] int SetShareMode(IntPtr deviceId, IntPtr mode);

    [PreserveSig]
    int GetPropertyValue(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceId, int fxStore, ref NativePropertyKey key, out NativePropVariant value);

    [PreserveSig]
    int SetPropertyValue(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceId, int fxStore, ref NativePropertyKey key, ref NativePropVariant value);

    [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, EndpointRole role);

    [PreserveSig] int SetEndpointVisibility(IntPtr deviceId, int visible);
}

[ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
internal class PolicyConfigClient;

/// <summary>Creates and releases the policy config COM object around each call.</summary>
internal static class PolicyConfig
{
    public static void SetDefaultEndpoint(string deviceId, EndpointRole role) =>
        Use(policy => Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(deviceId, role)));

    /// <summary>Throws <see cref="UnauthorizedAccessException"/> when Windows refuses the write.</summary>
    public static void SetPropertyValue(string deviceId, NativePropertyKey key, NativePropVariant value) =>
        Use(policy => Marshal.ThrowExceptionForHR(policy.SetPropertyValue(deviceId, 0, ref key, ref value)));

    public static object? GetPropertyValue(string deviceId, NativePropertyKey key) => Use(policy =>
    {
        Marshal.ThrowExceptionForHR(policy.GetPropertyValue(deviceId, 0, ref key, out var value));
        try
        {
            return value.ToObject();
        }
        finally
        {
            NativePropVariant.PropVariantClear(ref value);
        }
    });

    private static void Use(Action<IPolicyConfig> action) => Use<object?>(policy =>
    {
        action(policy);
        return null;
    });

    private static T Use<T>(Func<IPolicyConfig, T> action)
    {
        var policy = (IPolicyConfig)new PolicyConfigClient();
        try
        {
            return action(policy);
        }
        finally
        {
            Marshal.ReleaseComObject(policy);
        }
    }
}
