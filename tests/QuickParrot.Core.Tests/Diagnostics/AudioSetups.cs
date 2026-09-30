using QuickParrot.Core.Devices;
using QuickParrot.Core.Diagnostics;

namespace QuickParrot.Core.Tests.Diagnostics;

/// <summary>A healthy VB-CABLE setup to break one piece at a time.</summary>
internal static class AudioSetups
{
    public static readonly AudioDeviceInfo Headphones = new("hp", "Headphones (USB DAC)", AudioDeviceState.Active);
    public static readonly AudioDeviceInfo Speakers = new("spk", "Speakers (Realtek)", AudioDeviceState.Active);
    public static readonly AudioDeviceInfo CableInput = new("cable-in", "CABLE Input (VB-Audio Virtual Cable)", AudioDeviceState.Active);
    public static readonly CaptureDeviceInfo RealMic = new("mic", "Microphone (USB Audio Device)", AudioDeviceState.Active, true);
    public static readonly CaptureDeviceInfo CableOutput = new("cable-out", "CABLE Output (VB-Audio Virtual Cable)", AudioDeviceState.Active, false);

    public static AudioSetupSnapshot Healthy => new()
    {
        RenderDevices = [Headphones, CableInput, Speakers],
        CaptureDevices = [RealMic, CableOutput],
        Defaults = new DefaultEndpoints("hp", "hp", "hp", "cable-out", "cable-out", "cable-out"),
        Listen = ById(("mic", new ListenSetting(true, "cable-in")), ("cable-out", new ListenSetting(false, null))),
        Levels = ById(
            ("hp", new EndpointLevel(false, 0.5f)),
            ("cable-in", new EndpointLevel(false, 1f)),
            ("mic", new EndpointLevel(false, 0.6f)),
            ("cable-out", new EndpointLevel(false, 1f))),
        CommunicationsDucking = CommunicationsDucking.DoNothing,
    };

    public static AudioSetupSnapshot WithListen(this AudioSetupSnapshot snapshot, string id, ListenSetting? setting)
    {
        var listen = ById(snapshot.Listen.Select(p => (p.Key, p.Value)).ToArray());
        if (setting is null)
            listen.Remove(id);
        else
            listen[id] = setting;

        return snapshot with { Listen = listen };
    }

    public static AudioSetupSnapshot WithLevel(this AudioSetupSnapshot snapshot, string id, EndpointLevel? level)
    {
        var levels = ById(snapshot.Levels.Select(p => (p.Key, p.Value)).ToArray());
        if (level is null)
            levels.Remove(id);
        else
            levels[id] = level;

        return snapshot with { Levels = levels };
    }

    public static Dictionary<string, T> ById<T>(params (string Id, T Value)[] entries)
    {
        var map = AudioSetupSnapshot.EmptyById<T>();
        foreach (var (id, value) in entries)
            map[id] = value;

        return map;
    }

    public static IReadOnlyList<string> Ids(this IEnumerable<DiagnosticFinding> findings) => findings.Select(f => f.Id).ToList();
}
