using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LabelZoom.Sdk.Conversion;

namespace LabelZoom.Sdk
{
    /// <summary>
    /// Client for the LabelZoom conversion API.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Thread-safe and intended to be long-lived. Create one per application, not one per request.
    /// </para>
    /// <para>
    /// <b>An API key is optional.</b> Constructed without one, the client uses the anonymous free
    /// tier: watermarked output, first label only, a 1 MB request cap, and no multi-page,
    /// JSON-target, or image-to-image conversion.
    /// </para>
    /// <example>
    /// <code>
    /// using var client = new LabelZoomClient();
    /// var result = await client.Convert()
    ///     .FromZpl("^XA^FO20,20^A0N,28^FDHello^FS^XZ")
    ///     .ToPng()
    ///     .WithDpi(300)
    ///     .ExecuteAsync();
    /// result.Save("label.png");
    /// </code>
    /// </example>
    /// </remarks>
    public sealed class LabelZoomClient : IDisposable
    {
        private const string RequestIdHeader = "X-LZ-Request-Id";
        private const int MaxMessageLength = 512;

        private static readonly string SdkVersion =
            typeof(LabelZoomClient).GetTypeInfo().Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

        private readonly LabelZoomClientOptions _options;
        private readonly HttpClient _httpClient;
        private readonly bool _ownsHttpClient;
        private readonly string? _credential;
        private readonly string _userAgent;
        private readonly Random _jitter = new Random();

        private bool _disposed;

        /// <summary>Creates an anonymous client against the production API.</summary>
        public LabelZoomClient()
            : this(new LabelZoomClientOptions()) { }

        /// <summary>Creates a client authenticated with the given API key or JWT.</summary>
        /// <param name="apiKey">
        /// An <c>lz_live_</c>/<c>lz_test_</c> key or a JWT. Pass <c>null</c> to fall back to the
        /// <c>LABELZOOM_API_KEY</c> environment variable.
        /// </param>
        public LabelZoomClient(string? apiKey)
            : this(new LabelZoomClientOptions { ApiKey = apiKey }) { }

        /// <summary>Creates a client from an explicit options object.</summary>
        public LabelZoomClient(LabelZoomClientOptions options)
        {
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            // Snapshot so later mutation of the caller's object cannot change this client.
            _options = options.Clone();
            _credential = _options.ResolveCredential();

            if (_options.HttpClient is not null)
            {
                _httpClient = _options.HttpClient;
                _ownsHttpClient = false;
            }
            else
            {
                _httpClient = _options.HttpMessageHandler is null
                    ? new HttpClient()
                    : new HttpClient(_options.HttpMessageHandler, disposeHandler: false);
                _ownsHttpClient = true;
            }

            if (_options.Timeout.HasValue && _ownsHttpClient)
            {
                _httpClient.Timeout = _options.Timeout.Value;
            }

            // The server parses a "LabelZoomStudio/" prefix as a Studio version and silently
            // changes PDF handling for versions <= 1.8.2, so the SDK's own token must come first.
            var userAgent = $"labelzoom-dotnet-sdk/{SdkVersion}";
            if (!string.IsNullOrWhiteSpace(_options.UserAgentSuffix))
            {
                userAgent += " " + _options.UserAgentSuffix!.Trim();
            }

            _userAgent = userAgent;
        }

        /// <summary>Whether a credential was resolved. False means anonymous free-tier requests.</summary>
        public bool IsAuthenticated => _credential is not null;

        /// <summary>Starts building a conversion.</summary>
        public ConversionRequestBuilder Convert()
        {
            ThrowIfDisposed();
            return new ConversionRequestBuilder(this);
        }

        internal async Task<ConversionResult> ExecuteAsync(
            SourceFormat source,
            TargetFormat target,
            byte[] body,
            string contentType,
            ConversionParameters parameters,
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();

            var uri = BuildUri(source, target, parameters);
            var attempts = _options.MaxRetries + 1;

            for (var attempt = 1; ; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                HttpResponseMessage? response = null;
                try
                {
                    // A fresh request and content object per attempt: HttpRequestMessage cannot be
                    // reused, and a consumed HttpContent cannot be replayed.
                    using var request = new HttpRequestMessage(HttpMethod.Post, uri);
                    var content = new ByteArrayContent(body);
                    content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
                    request.Content = content;

                    // Accept must be */*. The server's `produces` list omits image/gif, image/bmp
                    // and image/jpeg, so naming the target's exact media type yields a 406 from
                    // content negotiation before the handler ever runs.
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
                    request.Headers.TryAddWithoutValidation("User-Agent", _userAgent);

                    if (_credential is not null)
                    {
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _credential);
                    }

                    response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

                    if (response.IsSuccessStatusCode)
                    {
                        return await BuildResultAsync(response).ConfigureAwait(false);
                    }

                    var error = await BuildExceptionAsync(response).ConfigureAwait(false);

                    if (attempt >= attempts || !IsRetryable(response.StatusCode))
                    {
                        throw error;
                    }

                    var retryAfter = (error as LabelZoomRateLimitedException)?.RetryAfterSeconds;
                    await DelayAsync(attempt, retryAfter, cancellationToken).ConfigureAwait(false);
                }
                catch (HttpRequestException) when (attempt < attempts)
                {
                    // Transport-level failure: connection refused, DNS, TLS, dropped socket.
                    await DelayAsync(attempt, null, cancellationToken).ConfigureAwait(false);
                }
                catch (TaskCanceledException) when (attempt < attempts && !cancellationToken.IsCancellationRequested)
                {
                    // HttpClient surfaces its own timeout as TaskCanceledException. The guard
                    // distinguishes that from the caller actually cancelling.
                    await DelayAsync(attempt, null, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    response?.Dispose();
                }
            }
        }

        internal Uri BuildUri(SourceFormat source, TargetFormat target, ConversionParameters parameters)
        {
            var path = $"{_options.BaseUrl}/api/v2/convert/{source.ToWireToken()}/to/{target.ToWireToken()}";

            var query = new List<string>();
            var json = parameters.ToJson();
            if (json is not null)
            {
                query.Add("params=" + Uri.EscapeDataString(json));
            }

            foreach (var pair in parameters.RawQueryParameters)
            {
                query.Add(Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value));
            }

            // No options set means no query string at all, not an empty "?params={}".
            return new Uri(query.Count == 0 ? path : path + "?" + string.Join("&", query));
        }

        private async Task<ConversionResult> BuildResultAsync(HttpResponseMessage response)
        {
            var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            var contentType = response.Content.Headers.ContentType;

            Encoding encoding = Encoding.UTF8;
            if (!string.IsNullOrEmpty(contentType?.CharSet))
            {
                try
                {
                    encoding = Encoding.GetEncoding(contentType!.CharSet!.Trim('"'));
                }
                catch (ArgumentException)
                {
                    // An unrecognized charset is not worth failing an otherwise good conversion.
                    encoding = Encoding.UTF8;
                }
            }

            return new ConversionResult(
                bytes,
                contentType?.ToString(),
                (int)response.StatusCode,
                ReadRequestId(response),
                encoding);
        }

        private async Task<LabelZoomException> BuildExceptionAsync(HttpResponseMessage response)
        {
            string rawBody;
            try
            {
                rawBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                rawBody = string.Empty;
            }

            var message = ExtractMessage(rawBody, response.ReasonPhrase);
            var requestId = ReadRequestId(response);
            var status = response.StatusCode;

            return (int)status switch
            {
                400 => new LabelZoomBadRequestException(message, requestId, rawBody),
                401 => new LabelZoomUnauthorizedException(message, requestId, rawBody),
                403 => new LabelZoomForbiddenException(
                    message, requestId, rawBody, IsPaidFeatureMessage(message)),
                404 => new LabelZoomNotFoundException(message, requestId, rawBody),
                413 => new LabelZoomPayloadTooLargeException(message, requestId, rawBody),
                429 => new LabelZoomRateLimitedException(
                    message, requestId, rawBody, ReadRetryAfterSeconds(response)),
                >= 500 => new LabelZoomServerException(status, message, requestId, rawBody),
                _ => new LabelZoomException(status, message, requestId, rawBody),
            };
        }

        /// <summary>
        /// Pulls the human-readable detail out of an error body.
        /// </summary>
        /// <remarks>
        /// Both error shapes in play put it on <c>message</c>: the gateway returns
        /// <c>{"message": "..."}</c> and Spring returns
        /// <c>{"timestamp","status","error","message","path"}</c>. Anything else — a rate-limit body
        /// keyed on <c>error</c>, an HTML 502, a truncated JSON fragment — falls through to the raw
        /// text, then to the reason phrase.
        /// </remarks>
        internal static string ExtractMessage(string? rawBody, string? reasonPhrase)
        {
            if (!string.IsNullOrWhiteSpace(rawBody))
            {
                try
                {
                    using var document = JsonDocument.Parse(rawBody!);
                    if (document.RootElement.ValueKind == JsonValueKind.Object &&
                        document.RootElement.TryGetProperty("message", out var messageElement) &&
                        messageElement.ValueKind == JsonValueKind.String)
                    {
                        var parsed = messageElement.GetString();
                        if (!string.IsNullOrWhiteSpace(parsed))
                        {
                            return Truncate(parsed!);
                        }
                    }
                }
                catch (JsonException)
                {
                    // Not JSON, or malformed. The raw body is still the most useful thing we have.
                }

                return Truncate(rawBody!.Trim());
            }

            return string.IsNullOrWhiteSpace(reasonPhrase)
                ? "The LabelZoom API returned an error with no response body."
                : reasonPhrase!;
        }

        private static string Truncate(string value) =>
            value.Length <= MaxMessageLength ? value : value.Substring(0, MaxMessageLength);

        private static bool IsPaidFeatureMessage(string message) =>
            message.IndexOf("paid feature", StringComparison.OrdinalIgnoreCase) >= 0;

        private static bool IsRetryable(HttpStatusCode status) =>
            (int)status == 429 || (int)status >= 500;

        /// <summary>
        /// Reads <c>X-LZ-Request-Id</c> case-insensitively.
        /// </summary>
        /// <remarks>
        /// The gateway sets <c>X-LZ-Request-Id</c> but CORS-exposes it as <c>X-LZ-Request-ID</c>.
        /// <see cref="HttpHeaders"/> is already case-insensitive; the explicit fallback guards
        /// against a stack that is not.
        /// </remarks>
        private static string? ReadRequestId(HttpResponseMessage response)
        {
            if (response.Headers.TryGetValues(RequestIdHeader, out var values))
            {
                return values.FirstOrDefault();
            }

            foreach (var header in response.Headers)
            {
                if (string.Equals(header.Key, RequestIdHeader, StringComparison.OrdinalIgnoreCase))
                {
                    return header.Value.FirstOrDefault();
                }
            }

            return null;
        }

        private static int? ReadRetryAfterSeconds(HttpResponseMessage response)
        {
            var retryAfter = response.Headers.RetryAfter;
            if (retryAfter is null)
            {
                return null;
            }

            if (retryAfter.Delta.HasValue)
            {
                return (int)Math.Ceiling(retryAfter.Delta.Value.TotalSeconds);
            }

            if (retryAfter.Date.HasValue)
            {
                var seconds = (retryAfter.Date.Value - DateTimeOffset.UtcNow).TotalSeconds;
                return seconds > 0 ? (int)Math.Ceiling(seconds) : 0;
            }

            return null;
        }

        /// <summary>
        /// Waits before the next attempt: 1s, 2s, 4s, with full jitter, overridden by a longer
        /// <c>Retry-After</c>.
        /// </summary>
        private Task DelayAsync(int attempt, int? retryAfterSeconds, CancellationToken cancellationToken)
        {
            var backoffSeconds = Math.Pow(2, attempt - 1);
            var delay = TimeSpan.FromSeconds(backoffSeconds);

            if (_options.UseJitter)
            {
                double next;
                lock (_jitter)
                {
                    next = _jitter.NextDouble();
                }

                delay = TimeSpan.FromSeconds(backoffSeconds * next);
            }

            // The server knows better than the backoff curve when it tells us how long to wait.
            if (retryAfterSeconds.HasValue)
            {
                var serverDelay = TimeSpan.FromSeconds(retryAfterSeconds.Value);
                if (serverDelay > delay)
                {
                    delay = serverDelay;
                }
            }

            var sleep = _options.SleepAsync;
            return sleep is null ? Task.Delay(delay, cancellationToken) : sleep(delay, cancellationToken);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(LabelZoomClient));
            }
        }

        /// <summary>Disposes the internally created <see cref="HttpClient"/>, if any.</summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_ownsHttpClient)
            {
                _httpClient.Dispose();
            }
        }
    }
}
