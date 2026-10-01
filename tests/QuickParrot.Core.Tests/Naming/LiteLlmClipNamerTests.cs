using System.Net;
using QuickParrot.Core.Naming;

namespace QuickParrot.Core.Tests.Naming;

public class LiteLlmClipNamerTests
{
    private static readonly LiteLlmOptions Options = new("https://litellm.example.com", "secret-key", "whisper-1", "gpt-4o-mini");

    private static ReadOnlyMemory<float> SampleAudio(int frames = 4_000) =>
        Enumerable.Range(0, frames).Select(i => (float)Math.Sin(i * 0.1)).ToArray();

    private static LiteLlmClipNamer CreateNamer(FakeHttpMessageHandler handler, LiteLlmOptions? options = null, TimeSpan? overallTimeout = null) =>
        new(new HttpClient(handler), options ?? Options, overallTimeout);

    private static string TranscriptionJson(string text) => $$"""{ "text": "{{text}}" }""";

    private static string ChatJson(string content) =>
        $$"""{ "choices": [ { "message": { "content": {{System.Text.Json.JsonSerializer.Serialize(content)}} } } ] }""";

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status) { Content = new StringContent(body) };

    [Fact]
    public async Task SuggestAsync_SendsExpectedTranscriptionRequest()
    {
        var handler = FakeHttpMessageHandler.Json(request => request.RequestUri!.ToString().Contains("transcriptions")
            ? (HttpStatusCode.OK, TranscriptionJson("hello there"))
            : (HttpStatusCode.OK, ChatJson("Hello There")));
        var namer = CreateNamer(handler);

        await namer.SuggestAsync(SampleAudio(), 48_000, 1, CancellationToken.None);

        var transcriptionRequest = handler.Requests[0];
        Assert.Equal("https://litellm.example.com/v1/audio/transcriptions", transcriptionRequest.RequestUri!.ToString());
        Assert.Equal("Bearer", transcriptionRequest.Headers.Authorization!.Scheme);
        Assert.Equal("secret-key", transcriptionRequest.Headers.Authorization!.Parameter);

        var body = handler.RequestBodies[0];
        Assert.Contains("name=model", body);
        Assert.Contains("whisper-1", body);
        Assert.Contains("name=response_format", body);
        Assert.Contains("json", body);
        Assert.Contains("name=file; filename=clip.wav", body);
    }

    [Fact]
    public async Task SuggestAsync_SendsExpectedChatRequest_AfterTranscription()
    {
        var handler = FakeHttpMessageHandler.Json(request => request.RequestUri!.ToString().Contains("transcriptions")
            ? (HttpStatusCode.OK, TranscriptionJson("hello there"))
            : (HttpStatusCode.OK, ChatJson("Hello There")));
        var namer = CreateNamer(handler);

        await namer.SuggestAsync(SampleAudio(), 48_000, 1, CancellationToken.None);

        var chatRequest = handler.Requests[1];
        Assert.Equal("https://litellm.example.com/v1/chat/completions", chatRequest.RequestUri!.ToString());
        Assert.Equal("Bearer", chatRequest.Headers.Authorization!.Scheme);

        var body = handler.RequestBodies[1];
        Assert.Contains("\"model\":\"gpt-4o-mini\"", body);
        Assert.Contains("hello there", body);
        Assert.Contains("\"temperature\":0.2", body);
        Assert.Contains("\"max_completion_tokens\":60", body);
        Assert.DoesNotContain("\"max_tokens\"", body);
        Assert.Contains("Transcript:\\nhello there", body);
    }

    [Theory]
    [InlineData("https://litellm.example.com")]
    [InlineData("https://litellm.example.com/")]
    [InlineData("https://litellm.example.com/v1")]
    [InlineData("https://litellm.example.com/v1/")]
    public async Task SuggestAsync_JoinsBaseUrlRegardlessOfTrailingSlashOrV1(string baseUrl)
    {
        var handler = FakeHttpMessageHandler.Json(_ => (HttpStatusCode.OK, TranscriptionJson("")));
        var namer = CreateNamer(handler, Options with { BaseUrl = baseUrl });

        await namer.SuggestAsync(SampleAudio(), 48_000, 1, CancellationToken.None);

        Assert.Equal("https://litellm.example.com/v1/audio/transcriptions", handler.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task SuggestAsync_NoApiKey_OmitsAuthorizationHeader()
    {
        var handler = FakeHttpMessageHandler.Json(_ => (HttpStatusCode.OK, TranscriptionJson("")));
        var namer = CreateNamer(handler, Options with { ApiKey = null });

        await namer.SuggestAsync(SampleAudio(), 48_000, 1, CancellationToken.None);

        Assert.Null(handler.Requests[0].Headers.Authorization);
    }

    [Fact]
    public async Task SuggestAsync_HappyPath_ReturnsTranscriptAndCleanedName()
    {
        var handler = FakeHttpMessageHandler.Json(request => request.RequestUri!.ToString().Contains("transcriptions")
            ? (HttpStatusCode.OK, TranscriptionJson("get down mister president"))
            : (HttpStatusCode.OK, ChatJson("\"Get Down, Mr. President.wav\"")));
        var namer = CreateNamer(handler);

        var result = await namer.SuggestAsync(SampleAudio(), 48_000, 1, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Get Down, Mr. President", result.Name);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task SuggestAsync_EmptyTranscript_ReturnsNullNameWithReason_AndSkipsChatCall()
    {
        var handler = FakeHttpMessageHandler.Json(_ => (HttpStatusCode.OK, TranscriptionJson("")));
        var namer = CreateNamer(handler);

        var result = await namer.SuggestAsync(SampleAudio(), 48_000, 1, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Null(result.Name);
        Assert.NotNull(result.Error);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task SuggestAsync_TranscriptionUnauthorized_ReturnsNullNameWithHttpStatusInMessage()
    {
        var handler = FakeHttpMessageHandler.Json(_ => (HttpStatusCode.Unauthorized, "{}"));
        var namer = CreateNamer(handler);

        var result = await namer.SuggestAsync(SampleAudio(), 48_000, 1, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Null(result.Name);
        Assert.Contains("401", result.Error);
    }

    [Fact]
    public async Task SuggestAsync_ChatServiceError_ReturnsNullNameWithHttpStatusInMessage()
    {
        var handler = FakeHttpMessageHandler.Json(request => request.RequestUri!.ToString().Contains("transcriptions")
            ? (HttpStatusCode.OK, TranscriptionJson("hello"))
            : (HttpStatusCode.InternalServerError, "oops"));
        var namer = CreateNamer(handler);

        var result = await namer.SuggestAsync(SampleAudio(), 48_000, 1, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Null(result.Name);
        Assert.StartsWith("Naming service returned 500", result.Error);
    }

    [Fact]
    public async Task SuggestAsync_ChatRejectsTemperature_RetriesWithoutItAndSucceeds()
    {
        var chatAttempts = 0;
        var handler = new FakeHttpMessageHandler(async (request, ct) =>
        {
            if (request.RequestUri!.ToString().Contains("transcriptions"))
                return Json(HttpStatusCode.OK, TranscriptionJson("hello"));

            chatAttempts++;
            var body = await request.Content!.ReadAsStringAsync(ct);
            if (chatAttempts == 1)
            {
                Assert.Contains("\"temperature\"", body);
                return Json(HttpStatusCode.BadRequest, """{ "error": { "message": "Unsupported value: 'temperature'" } }""");
            }

            Assert.DoesNotContain("\"temperature\"", body);
            return Json(HttpStatusCode.OK, ChatJson("Retried Name"));
        });
        var namer = CreateNamer(handler);

        var result = await namer.SuggestAsync(SampleAudio(), 48_000, 1, CancellationToken.None);

        Assert.Equal(2, chatAttempts);
        Assert.Equal("Retried Name", result.Name);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task SuggestAsync_ChatRejectsSomethingElse_DoesNotRetry()
    {
        var chatAttempts = 0;
        var handler = FakeHttpMessageHandler.Json(request =>
        {
            if (request.RequestUri!.ToString().Contains("transcriptions"))
                return (HttpStatusCode.OK, TranscriptionJson("hello"));

            chatAttempts++;
            return (HttpStatusCode.BadRequest, """{ "error": { "message": "model not found" } }""");
        });
        var namer = CreateNamer(handler);

        var result = await namer.SuggestAsync(SampleAudio(), 48_000, 1, CancellationToken.None);

        Assert.Equal(1, chatAttempts);
        Assert.Null(result.Name);
        Assert.Contains("400", result.Error);
    }

    [Fact]
    public async Task SuggestAsync_MalformedTranscriptionJson_ReturnsNullNameWithMessage()
    {
        var handler = FakeHttpMessageHandler.Json(_ => (HttpStatusCode.OK, "not json"));
        var namer = CreateNamer(handler);

        var result = await namer.SuggestAsync(SampleAudio(), 48_000, 1, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Null(result.Name);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task SuggestAsync_MalformedChatJson_ReturnsNullNameWithMessage()
    {
        var handler = FakeHttpMessageHandler.Json(request => request.RequestUri!.ToString().Contains("transcriptions")
            ? (HttpStatusCode.OK, TranscriptionJson("hello"))
            : (HttpStatusCode.OK, """{ "choices": [] }"""));
        var namer = CreateNamer(handler);

        var result = await namer.SuggestAsync(SampleAudio(), 48_000, 1, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Null(result.Name);
        Assert.NotNull(result.Error);
    }

    [Theory]
    [InlineData("\"Holy Cow\"", "Holy Cow")]
    [InlineData("'Great Scott'", "Great Scott")]
    [InlineData("Nice Shot.wav", "Nice Shot")]
    [InlineData("  Extra   Space   Here  ", "Extra Space Here")]
    [InlineData("Trailing Period.", "Trailing Period")]
    public async Task SuggestAsync_CleansUpModelReply(string rawReply, string expectedName)
    {
        var handler = FakeHttpMessageHandler.Json(request => request.RequestUri!.ToString().Contains("transcriptions")
            ? (HttpStatusCode.OK, TranscriptionJson("hello"))
            : (HttpStatusCode.OK, ChatJson(rawReply)));
        var namer = CreateNamer(handler);

        var result = await namer.SuggestAsync(SampleAudio(), 48_000, 1, CancellationToken.None);

        Assert.Equal(expectedName, result.Name);
    }

    [Fact]
    public async Task SuggestAsync_UsesOnlyTheFirstNonEmptyLineOfTheReply()
    {
        var handler = FakeHttpMessageHandler.Json(request => request.RequestUri!.ToString().Contains("transcriptions")
            ? (HttpStatusCode.OK, TranscriptionJson("hello there"))
            : (HttpStatusCode.OK, ChatJson("\n\nHoly Cow\nThat was a good one.")));
        var namer = CreateNamer(handler);

        var result = await namer.SuggestAsync(SampleAudio(), 48_000, 1, CancellationToken.None);

        Assert.Equal("Holy Cow", result.Name);
    }

    [Fact]
    public async Task SuggestAsync_ClipsAudioLongerThanSixtySeconds()
    {
        string? capturedBody = null;
        var handler = FakeHttpMessageHandler.Json(request =>
        {
            if (request.RequestUri!.ToString().Contains("transcriptions"))
                return (HttpStatusCode.OK, TranscriptionJson(""));
            return (HttpStatusCode.OK, ChatJson("x"));
        });
        var namer = CreateNamer(handler);

        var ninetySecondsAt16kMono = new float[16_000 * 90];
        await namer.SuggestAsync(ninetySecondsAt16kMono, 16_000, 1, CancellationToken.None);
        capturedBody = handler.RequestBodies[0];

        // 60s at 16kHz mono, 16-bit PCM: data chunk is 60 * 16000 * 2 bytes; just assert it's well under the 90s size.
        Assert.True(capturedBody.Length < ninetySecondsAt16kMono.Length * 2);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task SuggestAsync_InvalidSampleRate_Throws(int sampleRate)
    {
        var namer = CreateNamer(FakeHttpMessageHandler.Json(_ => (HttpStatusCode.OK, "{}")));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            namer.SuggestAsync(SampleAudio(), sampleRate, 1, CancellationToken.None));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task SuggestAsync_InvalidChannelCount_Throws(int channels)
    {
        var namer = CreateNamer(FakeHttpMessageHandler.Json(_ => (HttpStatusCode.OK, "{}")));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            namer.SuggestAsync(SampleAudio(), 48_000, channels, CancellationToken.None));
    }

    [Fact]
    public void Constructor_RequiresBaseUrl()
    {
        Assert.Throws<ArgumentException>(() =>
            new LiteLlmClipNamer(new HttpClient(), Options with { BaseUrl = "" }));
    }

    [Theory]
    [InlineData("""["hello"]""")]
    [InlineData("""{ "text": 5 }""")]
    public async Task SuggestAsync_TranscriptionJsonOfTheWrongShape_ReturnsMessage_NeverThrows(string body)
    {
        var handler = FakeHttpMessageHandler.Json(_ => (HttpStatusCode.OK, body));
        var namer = CreateNamer(handler);

        var result = await namer.SuggestAsync(SampleAudio(), 48_000, 1, CancellationToken.None);

        Assert.Null(result.Name);
        Assert.Contains("unexpected response", result.Error);
    }

    [Theory]
    [InlineData("litellm.local:4000")]
    [InlineData("not a url")]
    [InlineData("ftp://litellm.example.com")]
    public async Task InvalidBaseUrl_IsReportedWithoutSendingOrThrowing(string baseUrl)
    {
        var handler = FakeHttpMessageHandler.Json(_ => (HttpStatusCode.OK, TranscriptionJson("hello")));
        var namer = CreateNamer(handler, Options with { BaseUrl = baseUrl });

        var suggestion = await namer.SuggestAsync(SampleAudio(), 48_000, 1, CancellationToken.None);
        var test = await namer.TestConnectionAsync(CancellationToken.None);

        Assert.Empty(handler.Requests);
        Assert.Contains("base URL", suggestion.Error);
        Assert.False(test.Success);
        Assert.Contains("base URL", test.Message);
    }

    [Fact]
    public async Task SuggestAsync_ExternalCancellation_ReturnsNullNameWithMessage_NeverThrows()
    {
        var handler = new FakeHttpMessageHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable");
        });
        var namer = CreateNamer(handler);
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(20));

        var result = await namer.SuggestAsync(SampleAudio(), 48_000, 1, cts.Token);

        Assert.NotNull(result);
        Assert.Null(result.Name);
        Assert.Contains("cancelled", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SuggestAsync_OverallTimeout_ReturnsNullNameWithMessage_NeverThrows()
    {
        var handler = new FakeHttpMessageHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable");
        });
        var namer = CreateNamer(handler, overallTimeout: TimeSpan.FromMilliseconds(20));

        var result = await namer.SuggestAsync(SampleAudio(), 48_000, 1, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Null(result.Name);
        Assert.Contains("timed out", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SuggestAsync_ConnectionFailure_ReturnsNullNameWithMessage_NeverThrows()
    {
        var handler = new FakeHttpMessageHandler((_, _) => throw new HttpRequestException("Connection refused"));
        var namer = CreateNamer(handler);

        var result = await namer.SuggestAsync(SampleAudio(), 48_000, 1, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Null(result.Name);
        Assert.Contains("Couldn't reach", result.Error);
    }

    [Fact]
    public async Task TestConnectionAsync_Success()
    {
        var handler = FakeHttpMessageHandler.Json(_ => (HttpStatusCode.OK, "{}"));
        var namer = CreateNamer(handler);

        var result = await namer.TestConnectionAsync(CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("https://litellm.example.com/v1/models", handler.Requests[0].RequestUri!.ToString());
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
    }

    [Fact]
    public async Task TestConnectionAsync_Failure_ReportsStatusCode()
    {
        var handler = FakeHttpMessageHandler.Json(_ => (HttpStatusCode.Unauthorized, "{}"));
        var namer = CreateNamer(handler);

        var result = await namer.TestConnectionAsync(CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("401", result.Message);
    }
}
