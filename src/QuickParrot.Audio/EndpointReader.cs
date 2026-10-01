using System.Collections.Immutable;
using System.Diagnostics;
using NAudio.CoreAudioApi;
using QuickParrot.Core.Devices;
using QuickParrot.Core.Diagnostics;

namespace QuickParrot.Audio;

/// <summary>Describes audio endpoints, strictly read-only: property stores are only ever opened for reading.</summary>
internal static class EndpointReader
{
    public const DeviceState ListedStates = DeviceState.Active | DeviceState.Disabled | DeviceState.Unplugged;

    /// <summary>
    /// Describes each endpoint of <paramref name="flow"/> in <paramref name="states"/>, passing it to
    /// <paramref name="select"/> while it's still open. Endpoints that can't be described (e.g. vanished) are skipped.
    /// </summary>
    public static ImmutableArray<T> Describe<T>(
        MMDeviceEnumerator enumerator, DataFlow flow, DeviceState states, Func<MMDevice, AudioDeviceInfo, T> select)
    {
        var results = ImmutableArray.CreateBuilder<T>();
        foreach (var device in enumerator.EnumerateAudioEndPoints(flow, states))
        {
            using (device)
            {
                if (TryDescribe(device) is { } info)
                    results.Add(select(device, info));
            }
        }

        return results.ToImmutable();
    }

    public static string? DefaultId(MMDeviceEnumerator enumerator, DataFlow flow, Role role)
    {
        if (!enumerator.TryGetDefaultAudioEndpoint(flow, role, out var device))
            return null;

        using (device)
            return device.ID;
    }

    /// <summary>The mic's "Listen to this device" setting, or null if it can't be read.</summary>
    public static ListenSetting? TryReadListen(MMDevice device)
    {
        try
        {
            var properties = device.Properties;
            var enabled = properties.Contains(ListenProperties.Enabled) ? properties[ListenProperties.Enabled].Value : null;
            var target = properties.Contains(ListenProperties.Target) ? properties[ListenProperties.Target].Value : null;
            return ListenSetting.FromProperties(enabled, target);
        }
        catch (Exception e)
        {
            Debug.WriteLine($"QuickParrot: couldn't read a mic's Listen setting: {e.Message}");
            return null;
        }
    }

    /// <summary>Whether "Listen to this device" is on, read without its target; false if it can't be read.</summary>
    public static bool ReadListenEnabled(MMDevice device)
    {
        try
        {
            var properties = device.Properties;
            return properties.Contains(ListenProperties.Enabled) && properties[ListenProperties.Enabled].Value is true;
        }
        catch (Exception e)
        {
            Debug.WriteLine($"QuickParrot: couldn't read whether a mic's Listen is on: {e.Message}");
            return false;
        }
    }

    // A device can vanish mid-enumeration, making its property store throw.
    private static AudioDeviceInfo? TryDescribe(MMDevice device)
    {
        try
        {
            return new AudioDeviceInfo(device.ID, device.FriendlyName, ToState(device.State));
        }
        catch (Exception e)
        {
            Debug.WriteLine($"QuickParrot: skipped an audio endpoint that couldn't be described: {e.Message}");
            return null;
        }
    }

    private static AudioDeviceState ToState(DeviceState state) => state switch
    {
        DeviceState.Active => AudioDeviceState.Active,
        DeviceState.Disabled => AudioDeviceState.Disabled,
        DeviceState.Unplugged => AudioDeviceState.Unplugged,
        _ => AudioDeviceState.NotPresent,
    };
}
