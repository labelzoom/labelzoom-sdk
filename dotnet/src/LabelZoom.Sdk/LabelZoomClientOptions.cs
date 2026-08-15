using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace LabelZoom.Sdk
{
    /// <summary>
    /// Configuration for <see cref="LabelZoomClient"/>. Every property has a working default;
    /// an options object with nothing set produces a valid anonymous client.
    /// </summary>
    public sealed class LabelZoomClientOptions
    {
        /// <summary>The production API host.</summary>
        public const string DefaultBaseUrl = "https://api.labelzoom.com";

        /// <summary>The environment variable consulted when <see cref="ApiKey"/> is not set.</summary>
        public const string ApiKeyEnvironmentVariable = "LABELZOOM_API_KEY";

        private string _baseUrl = DefaultBaseUrl;
        private int _maxRetries = 2;

        /// <summary>
        /// The bearer credential — an <c>lz_live_</c>/<c>lz_test_</c> API key or a JWT.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Leave this <c>null</c> to read <c>LABELZOOM_API_KEY</c> from the environment. Set it to
        /// an empty string to force anonymous mode and suppress that fallback.
        /// </para>
        /// <para>
        /// Authentication is <em>optional</em>. Without a credential the API serves a free tier:
        /// watermarked output, first label only, a 1 MB request cap, and no multi-page,
        /// JSON-target, or image-to-image conversion.
        /// </para>
        /// </remarks>
        public string? ApiKey { get; set; }

        /// <summary>
        /// API base URL. Defaults to <see cref="DefaultBaseUrl"/>. A path prefix is preserved, so
        /// a reverse proxy at <c>https://proxy.example.com/labelzoom</c> works as expected.
        /// </summary>
        public string BaseUrl
        {
            get => _baseUrl;
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    throw new ArgumentException("Base URL cannot be null or empty.", nameof(value));
                }

                _baseUrl = value.TrimEnd('/');
            }
        }

        /// <summary>Per-request timeout. Defaults to 100 seconds (the <see cref="HttpClient"/> default).</summary>
        public TimeSpan? Timeout { get; set; }

        /// <summary>
        /// Retries after the initial attempt. Defaults to 2, giving 3 attempts total. Set to 0 to
        /// disable retrying.
        /// </summary>
        /// <remarks>
        /// Only 429, 5xx, and transport-level failures are retried. Other 4xx responses are
        /// returned immediately — a malformed request will not become valid on a second try.
        /// </remarks>
        public int MaxRetries
        {
            get => _maxRetries;
            set
            {
                if (value < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), value, "MaxRetries cannot be negative.");
                }

                _maxRetries = value;
            }
        }

        /// <summary>
        /// An <see cref="HttpClient"/> to use instead of one created internally. You own its
        /// lifetime; the SDK will not dispose it.
        /// </summary>
        public HttpClient? HttpClient { get; set; }

        /// <summary>
        /// An <see cref="HttpMessageHandler"/> for the internally created <see cref="HttpClient"/>.
        /// Ignored when <see cref="HttpClient"/> is set. This is the seam tests use to stub HTTP.
        /// </summary>
        public HttpMessageHandler? HttpMessageHandler { get; set; }

        /// <summary>
        /// Appended to the SDK's own User-Agent, for identifying your application in support
        /// conversations.
        /// </summary>
        /// <remarks>
        /// The server parses a <c>LabelZoomStudio/</c> User-Agent prefix as a Studio version and
        /// changes PDF handling accordingly, so the SDK always emits its own product token first.
        /// </remarks>
        public string? UserAgentSuffix { get; set; }

        /// <summary>
        /// Whether to apply full jitter to retry backoff. Defaults to <c>true</c>. Turn it off to
        /// make retry timing deterministic in tests.
        /// </summary>
        public bool UseJitter { get; set; } = true;

        /// <summary>
        /// Replaces the delay between retry attempts. Defaults to <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.
        /// </summary>
        /// <remarks>
        /// Substitute a recording no-op in tests so retry behaviour can be asserted without
        /// spending the wall-clock time.
        /// </remarks>
        public Func<TimeSpan, CancellationToken, Task>? SleepAsync { get; set; }

        internal LabelZoomClientOptions Clone() => (LabelZoomClientOptions)MemberwiseClone();

        /// <summary>
        /// Resolves the effective credential: an explicit <see cref="ApiKey"/> wins; <c>null</c>
        /// falls back to the environment; an empty string forces anonymous.
        /// </summary>
        internal string? ResolveCredential()
        {
            if (ApiKey is null)
            {
                var fromEnvironment = Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable);
                return string.IsNullOrEmpty(fromEnvironment) ? null : fromEnvironment;
            }

            return ApiKey.Length == 0 ? null : ApiKey;
        }
    }
}
