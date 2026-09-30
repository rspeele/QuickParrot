using QuickParrot.Core.Diagnostics;

namespace QuickParrot.Core.Tests.Diagnostics;

public class RepairCommandLineTests
{
    private const string MicId = "{0.0.1.00000000}.{8feef6e5-eeee-41fc-808c-4093a95c17e3}";
    private const string CableId = "{0.0.0.00000000}.{a759b3f1-8b10-48ff-b378-bce563067bf3}";

    [Fact]
    public void ListenArguments_RoundTrip()
    {
        var args = RepairCommandLine.ListenArguments(MicId, CableId);

        Assert.True(RepairCommandLine.IsRepair(args));
        Assert.Equal(new ListenRepairRequest(MicId, CableId), RepairCommandLine.ParseListen(args));
    }

    [Fact]
    public void CommandLine_QuotesEachArgument()
    {
        Assert.Equal(
            $"\"--repair\" \"listen\" \"{MicId}\" \"{CableId}\"",
            RepairCommandLine.ToCommandLine(RepairCommandLine.ListenArguments(MicId, CableId)));
    }

    [Fact]
    public void FlagAndVerb_IgnoreCase()
    {
        Assert.NotNull(RepairCommandLine.ParseListen(["--REPAIR", "Listen", MicId, CableId]));
    }

    public static TheoryData<string[]> BadArguments => new()
    {
        Array.Empty<string>(),
        new[] { "--repair" },
        new[] { "--repair", "listen", MicId },
        new[] { "--repair", "listen", MicId, CableId, "extra" },
        new[] { "--repair", "mute", MicId, CableId },
        new[] { "--fix", "listen", MicId, CableId },
        new[] { "--repair", "listen", "mic", CableId },
        new[] { "--repair", "listen", MicId, "{0.0.0.00000000}.{a759b3f1}" },
        new[] { "--repair", "listen", MicId, CableId + "\" --evil" },
        new[] { "--repair", "listen", MicId, CableId + "\n" },
    };

    [Theory]
    [MemberData(nameof(BadArguments))]
    public void BadArguments_AreRejected(string[] args)
    {
        Assert.Null(RepairCommandLine.ParseListen(args));
    }

    [Fact]
    public void NormalStartup_IsNotARepair()
    {
        Assert.False(RepairCommandLine.IsRepair([]));
        Assert.False(RepairCommandLine.IsRepair(["C:\\sounds"]));
    }

    [Fact]
    public void ListenArguments_RejectsInvalidIds()
    {
        Assert.Throws<ArgumentException>(() => RepairCommandLine.ListenArguments("mic id", CableId));
    }
}
