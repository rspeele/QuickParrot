using QuickParrot.Core.Devices;

namespace QuickParrot.Core.Tests.Devices;

public class CableNamesTests
{
    [Theory]
    [InlineData("CABLE Output (VB-Audio Virtual Cable)", true)]
    [InlineData("CABLE-A Output (VB-Audio Cable A)", true)]
    [InlineData("VoiceMeeter Output (VB-Audio VoiceMeeter VAIO)", true)]
    [InlineData("Line 1 (Virtual Audio Cable)", true)]
    [InlineData("CABLE Input (Some Cable)", false)]
    [InlineData("Microphone (USB Audio Device)", false)]
    [InlineData("Microphone (I'm Fulla Schiit)", false)]
    public void RecognisesCableCaptureSides(string name, bool isCable)
    {
        Assert.Equal(isCable, CableNames.IsCableCapture(name));
    }

    [Theory]
    [InlineData("CABLE Input (VB-Audio Virtual Cable)", true)]
    [InlineData("CABLE Input (Some Cable)", true)]
    [InlineData("Line 1 (Virtual Audio Cable)", true)]
    [InlineData("Speakers (Realtek(R) Audio)", false)]
    public void RecognisesCableRenderSides(string name, bool isCable)
    {
        Assert.Equal(isCable, CableNames.IsCableRender(name));
    }

    [Theory]
    [InlineData("CABLE Input (VB-Audio Virtual Cable)", "CABLE Output (VB-Audio Virtual Cable)")]
    [InlineData("CABLE-A Input (VB-Audio Cable A)", "CABLE-A Output (VB-Audio Cable A)")]
    [InlineData("Line 1 (Virtual Audio Cable)", "Line 1 (Virtual Audio Cable)")]
    public void CapturePartnerOf_MapsPlaybackToRecordingSide(string render, string capture)
    {
        Assert.Equal(capture, CableNames.CapturePartnerOf(render));
    }
}
