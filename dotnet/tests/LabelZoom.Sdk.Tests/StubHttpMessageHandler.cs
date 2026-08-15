using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace LabelZoom.Sdk.Tests;

/// <summary>
/// Records outgoing requests and replays a scripted sequence of responses.
/// </summary>
/// <remarks>
/// No sockets, no local server, no third-party mocking dependency — the SDK exposes
/// <see cref="LabelZoomClientOptions.HttpMessageHandler"/> precisely so the offline suite can run
/// anywhere, including on fork pull requests with no secrets.
/// </remarks>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage>> _responses = new();

    public List<RecordedRequest> Requests { get; } = new();

    public RecordedRequest LastRequest => Requests[^1];

    public void Enqueue(HttpResponseMessage response) => _responses.Enqueue(() => response);

    public void EnqueueTransportError() =>
        _responses.Enqueue(() => throw new HttpRequestException("simulated transport failure"));

    public void Enqueue(
        HttpStatusCode status,
        string body,
        string? contentType = "application/json",
        IDictionary<string, string>? headers = null)
    {
        _responses.Enqueue(() =>
        {
            var response = new HttpResponseMessage(status)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body)),
            };

            if (contentType is not null)
            {
                response.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);
            }

            foreach (var header in headers ?? new Dictionary<string, string>())
            {
                // Content-Type is the only header of interest that lives on the content rather
                // than the response, and it is handled above.
                response.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            return response;
        });
    }

    public void EnqueueBytes(
        HttpStatusCode status,
        byte[] body,
        string contentType,
        IDictionary<string, string>? headers = null)
    {
        _responses.Enqueue(() =>
        {
            var response = new HttpResponseMessage(status) { Content = new ByteArrayContent(body) };
            response.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);
            foreach (var header in headers ?? new Dictionary<string, string>())
            {
                response.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            return response;
        });
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? Array.Empty<byte>()
            : await request.Content.ReadAsByteArrayAsync(cancellationToken);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in request.Headers)
        {
            headers[header.Key] = string.Join(", ", header.Value);
        }

        if (request.Content is not null)
        {
            foreach (var header in request.Content.Headers)
            {
                headers[header.Key] = string.Join(", ", header.Value);
            }
        }

        Requests.Add(new RecordedRequest(
            request.Method.Method, request.RequestUri!, headers, body));

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException(
                $"StubHttpMessageHandler received an unexpected request to {request.RequestUri}; " +
                "no more responses are queued.");
        }

        return _responses.Dequeue()();
    }
}

internal sealed record RecordedRequest(
    string Method,
    Uri RequestUri,
    IReadOnlyDictionary<string, string> Headers,
    byte[] Body)
{
    public string BodyText => Encoding.UTF8.GetString(Body);

    public string? Header(string name) => Headers.TryGetValue(name, out var value) ? value : null;

    public bool HasHeader(string name) => Headers.ContainsKey(name);

    /// <summary>
    /// The decoded, parsed <c>params</c> query value, or <c>null</c> when the request carried none.
    /// </summary>
    /// <remarks>
    /// Parsed rather than string-compared on purpose: JSON property order is not guaranteed, and
    /// asserting on an encoded query string would make these tests brittle for no benefit.
    /// </remarks>
    public JsonElement? Params
    {
        get
        {
            var raw = QueryValue("params");
            if (raw is null)
            {
                return null;
            }

            using var document = JsonDocument.Parse(raw);
            return document.RootElement.Clone();
        }
    }

    public string? QueryValue(string key)
    {
        var query = RequestUri.Query.TrimStart('?');
        if (query.Length == 0)
        {
            return null;
        }

        foreach (var pair in query.Split('&'))
        {
            var separator = pair.IndexOf('=');
            var name = separator < 0 ? pair : pair[..separator];
            if (Uri.UnescapeDataString(name) == key)
            {
                return separator < 0 ? string.Empty : Uri.UnescapeDataString(pair[(separator + 1)..]);
            }
        }

        return null;
    }
}
