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

    public static AudioSetupSnapshot WithListen(this AudioSetupSnapshot snapshot, string id, ListenSetting? setting) =>
        snapshot with { Listen = With(snapshot.Listen, id, setting) };

    public static AudioSetupSnapshot WithLevel(this AudioSetupSnapshot snapshot, string id, EndpointLevel? level) =>
        snapshot with { Levels = With(snapshot.Levels, id, level) };

    private static IReadOnlyDictionary<string, T> With<T>(IReadOnlyDictionary<string, T> map, string id, T? value)
        where T : class
    {
        var others = map.Where(p => !Endpoints.SameId(p.Key, id));
        return AudioSetupSnapshot.ById(value is null ? others : others.Append(KeyValuePair.Create(id, value)));
    }

    public static IReadOnlyDictionary<string, T> ById<T>(params (string Id, T Value)[] entries) =>
        AudioSetupSnapshot.ById(entries.Select(e => KeyValuePair.Create(e.Id, e.Value)));

    public static IReadOnlyList<string> Ids(this IEnumerable<DiagnosticFinding> findings) => findings.Select(f => f.Id).ToList();
}
