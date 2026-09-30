using QuickParrot.Core.Mic;

namespace QuickParrot.Core.Tests.Mic;

public class JsonMicRestoreStoreTests
{
    [Fact]
    public void RoundTrips()
    {
        var record = new MicRestoreRecord("{0.0.1.00000000}.{mic}", "Microphone (USB)", MicDuckMode.Attenuate, true, 0.75f);

        Assert.Equal(record, JsonMicRestoreStore.Deserialize(JsonMicRestoreStore.Serialize(record)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("""{ "deviceName": "no id", "mode": 1, "originalVolume": 0.5 }""")]
    [InlineData("""{ "deviceId": "mic", "mode": 0, "originalVolume": 0.5 }""")]
    [InlineData("""{ "deviceId": "mic", "mode": 9, "originalVolume": 0.5 }""")]
    [InlineData("""{ "deviceId": "mic", "mode": 1, "originalVolume": 3 }""")]
    public void CorruptOrNonsensicalRecords_AreIgnored(string json)
    {
        Assert.Null(JsonMicRestoreStore.Deserialize(json));
    }
}
