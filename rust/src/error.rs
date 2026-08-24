//! The error taxonomy.

use std::error::Error as StdError;
use std::fmt;

/// Caps the derived message. [`ApiError::raw_body`] keeps the whole body.
pub(crate) const MAX_MESSAGE_LENGTH: usize = 512;

/// Everything a conversion can fail with.
///
/// Rust has no inheritance, so rule `E4`'s "every API error shares one base type" is this
/// enum: `Err(Error::Api(e))` is the single arm that catches all of them, and
/// [`Error::Validation`] is deliberately a sibling rather than a member -- it reports a
/// request rejected locally, before any network call, which is a bug in the calling code
/// rather than a server response.
#[derive(Debug)]
#[non_exhaustive]
pub enum Error {
    /// The request was rejected locally. No HTTP request was made, and none will be retried.
    Validation(ValidationError),
    /// The API returned a non-2xx response.
    Api(ApiError),
    /// The request never got a response.
    Transport(TransportError),
}

/// A non-2xx response from the LabelZoom API.
#[derive(Debug, Clone)]
pub struct ApiError {
    /// Which of the API's error classes this is.
    pub kind: ApiErrorKind,
    /// The HTTP status code the API returned.
    pub status: u16,
    /// The human-readable detail, derived from the body and capped at 512 characters.
    pub message: String,
    /// The `X-LZ-Request-Id` response header, when the server sent one. Quote it to
    /// LabelZoom support -- it identifies the exact request server-side.
    pub request_id: Option<String>,
    /// The response body, untruncated. [`ApiError::message`] is derived from it; this is not.
    pub raw_body: String,
}

/// Which class of API error this is.
///
/// The per-status data lives on the variant that has it rather than on every error: only
/// a 403 can be a paywall, and only a rate limit carries a `Retry-After`.
#[derive(Debug, Clone, PartialEq)]
#[non_exhaustive]
pub enum ApiErrorKind {
    /// HTTP 400. The request was malformed or the conversion path is invalid.
    BadRequest,
    /// HTTP 401. The supplied credential was rejected.
    Unauthorized,
    /// HTTP 403. The credential is valid but not entitled to this operation.
    Forbidden {
        /// True when this 403 is a paywall rather than a permissions problem -- "JSON
        /// export is a paid feature" and friends. By far the most common anonymous-tier
        /// failure, so it gets a flag instead of leaving callers matching strings.
        is_paid_feature: bool,
    },
    /// HTTP 404. The conversion path does not exist.
    NotFound,
    /// HTTP 413. The body exceeded the tier's limit -- 1 MB on the anonymous free tier.
    PayloadTooLarge,
    /// HTTP 429. Too many requests.
    RateLimited {
        /// `Retry-After` in seconds, when the server sent one. The client already honours
        /// it during its own retries; this exposes it for callers doing theirs.
        retry_after_seconds: Option<f64>,
    },
    /// HTTP 5xx. Retried automatically before surfacing.
    ServerError,
    /// Any other non-2xx. A 406 from content negotiation is the one that shows up in
    /// practice.
    Other,
}

impl ApiError {
    /// Whether this is a 403 raised by the paywall rather than by permissions.
    pub fn is_paid_feature(&self) -> bool {
        matches!(
            self.kind,
            ApiErrorKind::Forbidden {
                is_paid_feature: true
            }
        )
    }

    /// `Retry-After` in seconds, if this is a rate limit that carried one.
    pub fn retry_after_seconds(&self) -> Option<f64> {
        match self.kind {
            ApiErrorKind::RateLimited {
                retry_after_seconds,
            } => retry_after_seconds,
            _ => None,
        }
    }
}

/// A request rejected before it left the process.
#[derive(Debug, Clone)]
pub struct ValidationError {
    /// The conversion parameter at fault, named as it appears on the wire.
    pub parameter: String,
    /// What was wrong with it.
    pub message: String,
}

/// The request never got a response: a dial failure, a timeout, a TLS error.
#[derive(Debug)]
pub struct TransportError {
    /// What the transport reported.
    pub message: String,
    source: Option<Box<dyn StdError + Send + Sync>>,
}

impl TransportError {
    /// Wraps a transport failure.
    pub fn new(message: impl Into<String>) -> Self {
        Self {
            message: message.into(),
            source: None,
        }
    }

    /// Wraps a transport failure, keeping the underlying cause reachable via
    /// [`std::error::Error::source`].
    pub fn with_source(
        message: impl Into<String>,
        source: impl StdError + Send + Sync + 'static,
    ) -> Self {
        Self {
            message: message.into(),
            source: Some(Box::new(source)),
        }
    }
}

impl Error {
    /// Builds a validation error for `parameter`.
    pub(crate) fn validation(parameter: impl Into<String>, message: impl Into<String>) -> Self {
        Self::Validation(ValidationError {
            parameter: parameter.into(),
            message: message.into(),
        })
    }

    /// The API error inside, if this is one.
    pub fn as_api(&self) -> Option<&ApiError> {
        match self {
            Self::Api(error) => Some(error),
            _ => None,
        }
    }
}

impl fmt::Display for Error {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Self::Validation(error) => write!(f, "{error}"),
            Self::Api(error) => write!(f, "{error}"),
            Self::Transport(error) => write!(f, "{error}"),
        }
    }
}

impl fmt::Display for ApiError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match &self.request_id {
            Some(id) => write!(f, "HTTP {}: {} (request {id})", self.status, self.message),
            None => write!(f, "HTTP {}: {}", self.status, self.message),
        }
    }
}

impl fmt::Display for ValidationError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        write!(f, "invalid {}: {}", self.parameter, self.message)
    }
}

impl fmt::Display for TransportError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str(&self.message)
    }
}

impl StdError for Error {
    fn source(&self) -> Option<&(dyn StdError + 'static)> {
        match self {
            Self::Transport(error) => error.source(),
            _ => None,
        }
    }
}

impl StdError for ApiError {}
impl StdError for ValidationError {}

impl StdError for TransportError {
    fn source(&self) -> Option<&(dyn StdError + 'static)> {
        self.source.as_ref().map(|source| source.as_ref() as _)
    }
}

impl From<ApiError> for Error {
    fn from(error: ApiError) -> Self {
        Self::Api(error)
    }
}

impl From<TransportError> for Error {
    fn from(error: TransportError) -> Self {
        Self::Transport(error)
    }
}

/// Maps a response onto the typed error for its status.
pub(crate) fn error_for_status(
    status: u16,
    message: String,
    request_id: Option<String>,
    raw_body: String,
    retry_after_seconds: Option<f64>,
) -> ApiError {
    let kind = match status {
        400 => ApiErrorKind::BadRequest,
        401 => ApiErrorKind::Unauthorized,
        403 => ApiErrorKind::Forbidden {
            is_paid_feature: message.to_lowercase().contains("paid feature"),
        },
        404 => ApiErrorKind::NotFound,
        413 => ApiErrorKind::PayloadTooLarge,
        429 => ApiErrorKind::RateLimited {
            retry_after_seconds,
        },
        500..=599 => ApiErrorKind::ServerError,
        _ => ApiErrorKind::Other,
    };

    ApiError {
        kind,
        status,
        message,
        request_id,
        raw_body,
    }
}

/// Pulls the human-readable detail out of an error body.
///
/// Both shapes in play put it on `message`: the gateway returns `{"message": "..."}` and
/// Spring returns `{timestamp, status, error, message, path}`. Anything else -- a
/// rate-limit body keyed on `error`, an HTML 502, a truncated JSON fragment -- falls
/// through to the raw text, then to the reason phrase.
pub(crate) fn extract_message(raw_body: &str, reason_phrase: Option<&str>) -> String {
    if !raw_body.trim().is_empty() {
        if let Ok(serde_json::Value::Object(parsed)) =
            serde_json::from_str::<serde_json::Value>(raw_body)
        {
            if let Some(detail) = parsed.get("message").and_then(serde_json::Value::as_str) {
                if !detail.trim().is_empty() {
                    return truncate(detail);
                }
            }
        }
        // Not JSON, or JSON without a usable message. The raw body is still the most
        // useful thing available, and a parse failure must not mask the HTTP error.
        return truncate(raw_body.trim());
    }

    match reason_phrase {
        Some(phrase) if !phrase.trim().is_empty() => phrase.to_owned(),
        _ => "The LabelZoom API returned an error with no response body.".to_owned(),
    }
}

/// Truncates by CHARACTER, not byte: slicing a multi-byte character in half would panic.
fn truncate(value: &str) -> String {
    match value.char_indices().nth(MAX_MESSAGE_LENGTH) {
        Some((index, _)) => value[..index].to_owned(),
        None => value.to_owned(),
    }
}

/// The HTTP reason phrase for a status.
///
/// Derived from a table rather than read off the response: rule E2's third fallback is
/// what `response/error-empty-body` (a 406 with no body) turns on, and a transport does
/// not reliably surface a phrase.
pub(crate) fn reason_phrase(status: u16) -> Option<&'static str> {
    Some(match status {
        400 => "Bad Request",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "Not Found",
        405 => "Method Not Allowed",
        406 => "Not Acceptable",
        408 => "Request Timeout",
        409 => "Conflict",
        413 => "Payload Too Large",
        415 => "Unsupported Media Type",
        422 => "Unprocessable Entity",
        429 => "Too Many Requests",
        500 => "Internal Server Error",
        501 => "Not Implemented",
        502 => "Bad Gateway",
        503 => "Service Unavailable",
        504 => "Gateway Timeout",
        _ => return None,
    })
}
