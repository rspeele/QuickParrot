using System.Net;

namespace QuickParrot.Core.Tests.Naming;

/// <summary>Routes requests to a per-test responder so no real network call is ever made.</summary>
public sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
    : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];
    public List<string> RequestBodies { get; } = [];

    public static FakeHttpMessageHandler Json(Func<HttpRequestMessage, (HttpStatusCode Status, string Body)> responder) =>
        new((request, _) =>
        {
            var (status, body) = responder(request);
            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent(body),
            };
            return Task.FromResult(response);
        });

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);

        // Latin1 maps each byte to one char, so multipart bodies with binary (WAV) payloads keep an exact,
        // round-trippable length and their ASCII headers/fields stay readable for Contains() assertions.
        var bytes = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        RequestBodies.Add(System.Text.Encoding.Latin1.GetString(bytes));

        return await responder(request, cancellationToken);
    }
}
