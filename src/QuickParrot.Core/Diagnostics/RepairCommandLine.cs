using System.Text.RegularExpressions;

namespace QuickParrot.Core.Diagnostics;

public enum RepairExitCode
{
    Success = 0,
    Failed = 1,
    InvalidArguments = 2,
}

public sealed record ListenRepairRequest(string MicId, string TargetId);

/// <summary>
/// The command line for QuickParrot's elevated one-shot helper, e.g. <c>--repair listen &lt;micId&gt; &lt;renderId&gt;</c>.
/// Arguments are validated strictly since they're acted on with administrator rights.
/// </summary>
public static partial class RepairCommandLine
{
    public const string Flag = "--repair";
    public const string ListenVerb = "listen";

    public static bool IsRepair(IReadOnlyList<string> args) =>
        args.Count > 0 && string.Equals(args[0], Flag, StringComparison.OrdinalIgnoreCase);

    /// <summary>Null unless the arguments are exactly a listen repair with two valid endpoint IDs.</summary>
    public static ListenRepairRequest? ParseListen(IReadOnlyList<string> args)
    {
        if (args.Count != 4 || !IsRepair(args) || !string.Equals(args[1], ListenVerb, StringComparison.OrdinalIgnoreCase))
            return null;

        return IsValidEndpointId(args[2]) && IsValidEndpointId(args[3]) ? new ListenRepairRequest(args[2], args[3]) : null;
    }

    public static IReadOnlyList<string> ListenArguments(string micId, string targetId)
    {
        if (!IsValidEndpointId(micId) || !IsValidEndpointId(targetId))
            throw new ArgumentException("Not a Windows audio endpoint ID.");

        return [Flag, ListenVerb, micId, targetId];
    }

    // Safe to quote naively: valid endpoint IDs contain no spaces or quotes.
    public static string ToCommandLine(IReadOnlyList<string> args) => string.Join(" ", args.Select(a => $"\"{a}\""));

    /// <summary>Like <c>{0.0.1.00000000}.{8feef6e5-eeee-41fc-808c-4093a95c17e3}</c>.</summary>
    public static bool IsValidEndpointId(string id) => EndpointIdPattern().IsMatch(id);

    public static string Describe(int exitCode) => exitCode switch
    {
        (int)RepairExitCode.Success => "it succeeded",
        (int)RepairExitCode.InvalidArguments => "the helper didn't understand the request",
        _ => "Windows refused the change",
    };

    // \z, not $: $ would also accept a trailing newline.
    [GeneratedRegex(@"\A\{[0-9A-Fa-f.]+\}\.\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}\z")]
    private static partial Regex EndpointIdPattern();
}
