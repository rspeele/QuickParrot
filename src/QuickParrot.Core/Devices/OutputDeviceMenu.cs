namespace QuickParrot.Core.Devices;

/// <summary>One entry in an output-device dropdown; an empty <see cref="Id"/> means "auto-detect" / "Windows default".</summary>
public sealed record OutputDeviceChoice(string Id, string Name);

public sealed record OutputDeviceChoices(IReadOnlyList<OutputDeviceChoice> Cable, IReadOnlyList<OutputDeviceChoice> Monitor);

/// <summary>Builds the cable and monitor dropdowns: an automatic entry first, then every present render device.</summary>
public static class OutputDeviceMenu
{
    public static OutputDeviceChoices Build(IReadOnlyList<AudioDeviceInfo> devices)
    {
        var listed = devices
            .Where(d => d.State != AudioDeviceState.NotPresent)
            .Select(d => new OutputDeviceChoice(d.Id, d.IsActive ? d.Name : $"{d.Name} ({d.State.ToString().ToLowerInvariant()})"))
            .ToList();

        return new OutputDeviceChoices(
            [new OutputDeviceChoice("", "(Auto-detect cable)"), .. listed],
            [new OutputDeviceChoice("", "(Windows default output)"), .. listed]);
    }
}
