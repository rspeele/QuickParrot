using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace QuickParrot.Core.Naming;

/// <summary>
/// Suggests a clip name by transcribing audio (Whisper-compatible <c>/v1/audio/transcriptions</c>) and asking a
/// chat model (<c>/v1/chat/completions</c>) to turn the transcript into a short soundboard-style name.
/// </summary>
public sealed class LiteLlmClipNamer : IClipNamer
{
    private const int MaxTranscriptionSeconds = 60;
    private const int TargetSampleRate = 16_000;

    // Generous enough for a reasoning model's hidden preamble plus the actual name; only the first line is kept.
    private const int MaxNameTokens = 60;
    private static readonly TimeSpan DefaultOverallTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ConnectionTestTimeout = TimeSpan.FromSeconds(10);

    private const string InvalidBaseUrl = "The LiteLLM base URL isn't a valid http(s) address.";

    private const string SystemPrompt =
        "You name short audio clips for a gamer's soundboard. Given a transcript of speech, reply with ONLY a " +
        "short clip name: 2-6 words, Title Case, no surrounding quotes, no file extension, no trailing " +
        "punctuation. Prefer a funny or memorable phrase drawn from the quote itself when there is one.";

    private readonly HttpClient _httpClient;
    private readonly LiteLlmOptions _options;
    private readonly TimeSpan _overallTimeout;
    private readonly Uri? _baseUri;

    /// <summary><paramref name="overallTimeout"/> defaults to 20s; tests pass a short one to stay fast.</summary>
    public LiteLlmClipNamer(HttpClient httpClient, LiteLlmOptions options, TimeSpan? overallTimeout = null)
    {
        if (string.IsNullOrWhiteSpace(options.BaseUrl))
            throw new ArgumentException("Base URL must be set.", nameof(options));

        _httpClient = httpClient;
        _options = options;
        _overallTimeout = overallTimeout ?? DefaultOverallTimeout;
        _baseUri = ParseBaseUri(options.BaseUrl);
    }

    public async Task<ClipNameSuggestion?> SuggestAsync(
        ReadOnlyMemory<float> interleaved, int sampleRate, int channels, CancellationToken cancellationToken)
    {
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (channels <= 0)
            throw new ArgumentOutOfRangeException(nameof(channels));

        if (_baseUri is null)
            return new ClipNameSuggestion("", null, InvalidBaseUrl);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_overallTimeout);

        try
        {
            // Resampling a long clip takes a noticeable fraction of a second, so keep it off the caller's (UI) thread.
            var wav = await Task.Run(() => EncodeForTranscription(interleaved, sampleRate, channels), timeoutCts.Token)
                .ConfigureAwait(false);

            var (transcript, transcribeError) = await TranscribeAsync(wav, timeoutCts.Token).ConfigureAwait(false);
            if (transcribeError is not null)
                return new ClipNameSuggestion("", null, transcribeError);

            if (string.IsNullOrWhiteSpace(transcript))
                return new ClipNameSuggestion(transcript ?? "", null, "No speech was detected in the clip.");

            var (name, nameError) = await SuggestNameAsync(transcript, timeoutCts.Token).ConfigureAwait(false);
            if (nameError is not null)
                return new ClipNameSuggestion(transcript, null, nameError);

            return name is null
                ? new ClipNameSuggestion(transcript, null, "The naming service didn't return a usable name.")
                : new ClipNameSuggestion(transcript, name, null);
        }
        catch (OperationCanceledException)
        {
            var message = cancellationToken.IsCancellationRequested
                ? "Naming the clip was cancelled."
                : "Naming the clip timed out.";
            return new ClipNameSuggestion("", null, message);
        }
        catch (HttpRequestException e)
        {
            return new ClipNameSuggestion("", null, $"Couldn't reach the naming service: {e.Message}");
        }
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken)
    {
        if (_baseUri is null)
            return new ConnectionTestResult(false, InvalidBaseUrl);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(ConnectionTestTimeout);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, BuildUrl("models"));
            AddAuth(request);
            using var response = await _httpClient.SendAsync(request, timeoutCts.Token).ConfigureAwait(false);

            return response.IsSuccessStatusCode
                ? new ConnectionTestResult(true, "Connected successfully.")
                : new ConnectionTestResult(false, $"Server returned {(int)response.StatusCode} {response.ReasonPhrase}.");
        }
        catch (HttpRequestException e)
        {
            return new ConnectionTestResult(false, $"Couldn't reach the server: {e.Message}");
        }
        catch (OperationCanceledException)
        {
            return new ConnectionTestResult(false, cancellationToken.IsCancellationRequested ? "Cancelled." : "Timed out.");
        }
    }

    private async Task<(string? Transcript, string? Error)> TranscribeAsync(byte[] wavBytes, CancellationToken ct)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(wavBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(fileContent, "file", "clip.wav");
        content.Add(new StringContent(_options.TranscriptionModel), "model");
        content.Add(new StringContent("json"), "response_format");

        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUrl("audio/transcriptions"))
        {
            Content = content,
        };
        AddAuth(request);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException e)
        {
            return (null, $"Couldn't reach the transcription service: {e.Message}");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                return (null, $"Transcription service returned {(int)response.StatusCode} {response.ReasonPhrase}.");

            string body;
            try
            {
                body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            }
            catch (IOException)
            {
                return (null, "Couldn't read the transcription response.");
            }

            try
            {
                using var doc = JsonDocument.Parse(body);
                var text = doc.RootElement.TryGetProperty("text", out var textProp) ? textProp.GetString() : null;
                return (text ?? "", null);
            }
            catch (Exception e) when (e is JsonException or InvalidOperationException)
            {
                return (null, "Transcription service returned an unexpected response.");
            }
        }
    }

    private async Task<(string? Name, string? Error)> SuggestNameAsync(string transcript, CancellationToken ct)
    {
        var payload = new
        {
            model = _options.ChatModel,
            temperature = 0.2,
            max_tokens = MaxNameTokens,
            messages = new object[]
            {
                new { role = "system", content = SystemPrompt },
                new { role = "user", content = $"Transcript:\n{transcript}" },
            },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUrl("chat/completions"))
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };
        AddAuth(request);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException e)
        {
            return (null, $"Couldn't reach the naming service: {e.Message}");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                return (null, $"Naming service returned {(int)response.StatusCode} {response.ReasonPhrase}.");

            string body;
            try
            {
                body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            }
            catch (IOException)
            {
                return (null, "Couldn't read the naming response.");
            }

            try
            {
                using var doc = JsonDocument.Parse(body);
                var content = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString();
                return (CleanName(FirstNonEmptyLine(content)), null);
            }
            catch (Exception e) when (e is JsonException or InvalidOperationException or IndexOutOfRangeException or KeyNotFoundException)
            {
                return (null, "Naming service returned an unexpected response.");
            }
        }
    }

    private static byte[] EncodeForTranscription(ReadOnlyMemory<float> interleaved, int sampleRate, int channels)
    {
        var maxSamples = MaxTranscriptionSeconds * sampleRate * channels;
        var clipped = interleaved.Length <= maxSamples ? interleaved : interleaved[..maxSamples];
        var mono = AudioDownmixer.ToMono(clipped.Span, channels);
        return WavEncoder.EncodePcm16(AudioResampler.Resample(mono, sampleRate, TargetSampleRate), TargetSampleRate);
    }

    // A reasoning model may think out loud before the name; only its first non-blank line is ever usable.
    private static string? FirstNonEmptyLine(string? raw)
    {
        if (raw is null)
            return null;

        foreach (var line in raw.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0)
                return trimmed;
        }

        return null;
    }

    // Strips quotes/punctuation/extension the model sometimes adds despite instructions, and caps length.
    private static string? CleanName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var name = raw.Trim().Trim('"', '\'', '`', '.', ' ');
        name = Regex.Replace(name, @"\.(wav|mp3|ogg|flac|m4a)$", "", RegexOptions.IgnoreCase);
        name = Regex.Replace(name, @"\s+", " ").Trim();

        if (name.Length > 60)
            name = name[..60].TrimEnd();

        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    private void AddAuth(HttpRequestMessage request)
    {
        if (!string.IsNullOrEmpty(_options.ApiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
    }

    private Uri BuildUrl(string path) => new($"{_baseUri!.AbsoluteUri}/{path}");

    private static Uri? ParseBaseUri(string baseUrl)
    {
        baseUrl = baseUrl.Trim().TrimEnd('/');
        if (!baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            baseUrl += "/v1";

        return Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? uri
            : null;
    }
}
