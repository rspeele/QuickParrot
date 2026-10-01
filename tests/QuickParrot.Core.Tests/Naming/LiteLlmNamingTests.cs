using System.Net;
using QuickParrot.Core.Editing;
using QuickParrot.Core.Naming;
using QuickParrot.Core.Settings;

namespace QuickParrot.Core.Tests.Naming;

public class LiteLlmNamingTests
{
    private static readonly EditableAudio Clip = new(new float[4800], 48000, 1);

    private static readonly AppSettings Configured = new() { LiteLlmBaseUrl = "https://litellm.example.com" };

    private sealed class FailingProtector : IDpapiProtector
    {
        public string Protect(string plaintext) => plaintext;

        public bool TryUnprotect(string base64Ciphertext, out string plaintext)
        {
            plaintext = "";
            return false;
        }
    }

    private static FakeHttpMessageHandler NamingService() => FakeHttpMessageHandler.Json(request =>
        request.RequestUri!.ToString().Contains("transcriptions")
            ? (HttpStatusCode.OK, """{ "text": "hello there" }""")
            : (HttpStatusCode.OK, """{ "choices": [ { "message": { "content": "Hello There" } } ] }"""));

    [Fact]
    public void NotConfigured_HasNoSuggester()
    {
        var suggest = LiteLlmNaming.CreateSuggester(() => new AppSettings(), new FailingProtector(), new HttpClient(NamingService()), _ => { });

        Assert.Null(suggest);
    }

    [Fact]
    public async Task TurnedOffAfterOpening_SuggestsNothing_WithoutCallingTheService()
    {
        var settings = Configured;
        var handler = NamingService();
        var suggest = LiteLlmNaming.CreateSuggester(() => settings, new FailingProtector(), new HttpClient(handler), _ => { })!;

        settings = new AppSettings();

        Assert.Equal(new NameSuggestion(null), await suggest(Clip, CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task AnUndecryptableKey_WarnsAndStillAsksTheService()
    {
        var warnings = new List<string>();
        var handler = NamingService();
        var suggest = LiteLlmNaming.CreateSuggester(
            () => Configured with { LiteLlmApiKeyEncrypted = "garbage" }, new FailingProtector(), new HttpClient(handler), warnings.Add)!;

        var suggestion = await suggest(Clip, CancellationToken.None);

        Assert.Equal("Hello There", suggestion.Name);
        Assert.Contains("Couldn't decrypt", Assert.Single(warnings));
        Assert.Null(handler.Requests[0].Headers.Authorization);
    }
}
