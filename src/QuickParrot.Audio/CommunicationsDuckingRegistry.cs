using Microsoft.Win32;
using QuickParrot.Core.Diagnostics;

namespace QuickParrot.Audio;

/// <summary>The Sound control panel's Communications tab choice, per user.</summary>
internal static class CommunicationsDuckingRegistry
{
    private const string KeyPath = @"Software\Microsoft\Multimedia\Audio";
    private const string ValueName = "UserDuckingPreference";

    /// <summary>Null if it couldn't be read. Never throws.</summary>
    public static CommunicationsDucking? Read()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: false);
            return CommunicationsDuckingPreference.FromRegistryValue(key?.GetValue(ValueName));
        }
        catch (Exception)
        {
            return null;
        }
    }

    // Unverified whether a voice stream that's already ducking picks this up before it restarts.
    public static void Write(CommunicationsDucking value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
        key.SetValue(ValueName, (int)value, RegistryValueKind.DWord);
    }
}
