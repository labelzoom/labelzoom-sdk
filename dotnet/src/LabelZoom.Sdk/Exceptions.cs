using System;
using System.Net;

namespace LabelZoom.Sdk
{
    /// <summary>
    /// Base type for every error the LabelZoom API returns. Catch this to handle them all.
    /// </summary>
    public class LabelZoomException : Exception
    {
        internal LabelZoomException(
            HttpStatusCode statusCode, string message, string? requestId, string? rawBody)
            : base(message)
        {
            StatusCode = statusCode;
            RequestId = requestId;
            RawBody = rawBody;
        }

        /// <summary>The HTTP status code the API returned.</summary>
        public HttpStatusCode StatusCode { get; }

        /// <summary>
        /// The value of the <c>X-LZ-Request-Id</c> response header, if present. Quote this when
        /// contacting LabelZoom support — it identifies the exact request server-side.
        /// </summary>
        public string? RequestId { get; }

        /// <summary>
        /// The raw response body, untruncated. <see cref="Exception.Message"/> is derived from it
        /// and capped at 512 characters; this is not.
        /// </summary>
        public string? RawBody { get; }
    }

    /// <summary>HTTP 400. The request was malformed or the conversion path is invalid.</summary>
    public sealed class LabelZoomBadRequestException : LabelZoomException
    {
        internal LabelZoomBadRequestException(string message, string? requestId, string? rawBody)
            : base(HttpStatusCode.BadRequest, message, requestId, rawBody) { }
    }

    /// <summary>HTTP 401. The supplied credential was rejected.</summary>
    public sealed class LabelZoomUnauthorizedException : LabelZoomException
    {
        internal LabelZoomUnauthorizedException(string message, string? requestId, string? rawBody)
            : base(HttpStatusCode.Unauthorized, message, requestId, rawBody) { }
    }

    /// <summary>HTTP 403. The credential is valid but not entitled to this operation.</summary>
    public sealed class LabelZoomForbiddenException : LabelZoomException
    {
        internal LabelZoomForbiddenException(
            string message, string? requestId, string? rawBody, bool isPaidFeature)
            : base(HttpStatusCode.Forbidden, message, requestId, rawBody)
        {
            IsPaidFeature = isPaidFeature;
        }

        /// <summary>
        /// True when this 403 is a paywall rather than a permissions problem — for example
        /// "JSON export is a paid feature". This is by far the most common failure on the
        /// anonymous free tier, so it gets a flag instead of leaving callers to match strings.
        /// </summary>
        public bool IsPaidFeature { get; }
    }

    /// <summary>HTTP 404. The conversion path does not exist.</summary>
    public sealed class LabelZoomNotFoundException : LabelZoomException
    {
        internal LabelZoomNotFoundException(string message, string? requestId, string? rawBody)
            : base(HttpStatusCode.NotFound, message, requestId, rawBody) { }
    }

    /// <summary>
    /// HTTP 413. The request body exceeded the tier's limit — 1 MB on the anonymous free tier.
    /// </summary>
    public sealed class LabelZoomPayloadTooLargeException : LabelZoomException
    {
        internal LabelZoomPayloadTooLargeException(string message, string? requestId, string? rawBody)
            : base(HttpStatusCode.RequestEntityTooLarge, message, requestId, rawBody) { }
    }

    /// <summary>HTTP 429. Too many requests.</summary>
    public sealed class LabelZoomRateLimitedException : LabelZoomException
    {
        internal LabelZoomRateLimitedException(
            string message, string? requestId, string? rawBody, int? retryAfterSeconds)
            : base((HttpStatusCode)429, message, requestId, rawBody)
        {
            RetryAfterSeconds = retryAfterSeconds;
        }

        /// <summary>
        /// The <c>Retry-After</c> header in seconds, if the server sent one. The SDK already
        /// honours this during its own retries; this exposes it for callers doing their own.
        /// </summary>
        public int? RetryAfterSeconds { get; }
    }

    /// <summary>HTTP 5xx. Retried automatically before surfacing.</summary>
    public sealed class LabelZoomServerException : LabelZoomException
    {
        internal LabelZoomServerException(
            HttpStatusCode statusCode, string message, string? requestId, string? rawBody)
            : base(statusCode, message, requestId, rawBody) { }
    }

    /// <summary>
    /// A request was rejected locally, before any network call.
    /// </summary>
    /// <remarks>
    /// Deliberately <em>not</em> a <see cref="LabelZoomException"/>. This is a bug in the calling
    /// code, not a server response: it carries no status code, it is never retried, and a caller
    /// catching <see cref="LabelZoomException"/> to implement fallback behaviour should not
    /// swallow it.
    /// </remarks>
    public sealed class LabelZoomValidationException : ArgumentException
    {
        internal LabelZoomValidationException(string parameterName, string message)
            : base(message, parameterName) { }
    }
}
