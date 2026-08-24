//! The client, its builder, and the retry loop.

use crate::error::{error_for_status, extract_message, reason_phrase, Error, TransportError};
use crate::options::ConvertRequest;
use crate::result::ConversionResult;
use crate::transport::{HttpRequest, HttpResponse, Sleeper, ThreadSleeper, Transport};
use std::sync::Arc;
use std::time::Duration;

/// The production API host.
pub const DEFAULT_BASE_URL: &str = "https://api.labelzoom.com";

/// The environment variable consulted when no credential is configured.
pub const API_KEY_ENV_VAR: &str = "LABELZOOM_API_KEY";

/// This crate's version, as it appears in the User-Agent.
pub const VERSION: &str = env!("CARGO_PKG_VERSION");

const REQUEST_ID_HEADER: &str = "x-lz-request-id";

/// Converts labels through the LabelZoom API.
///
/// Authentication is optional. Without a credential the API serves a free tier:
/// watermarked output, the first label only, a 1 MB request cap, and no multi-page,
/// JSON-target or image-to-image conversion. Building a client with no key is therefore a
/// supported, tested path rather than an error.
#[derive(Clone)]
pub struct LabelZoomClient {
    base_url: String,
    credential: Option<String>,
    max_retries: u32,
    user_agent: String,
    transport: Arc<dyn Transport>,
    sleeper: Arc<dyn Sleeper>,
    jitter: bool,
}

impl std::fmt::Debug for LabelZoomClient {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        // The credential is deliberately reduced to a boolean: a Debug print of a client
        // is exactly the kind of thing that ends up in a log.
        f.debug_struct("LabelZoomClient")
            .field("base_url", &self.base_url)
            .field("authenticated", &self.credential.is_some())
            .field("max_retries", &self.max_retries)
            .field("user_agent", &self.user_agent)
            .finish_non_exhaustive()
    }
}

/// Configures a [`LabelZoomClient`].
pub struct ClientBuilder {
    // Two layers of Option on purpose (clippy::option_option is allowed for exactly this): the outer says whether the caller configured a
    // credential AT ALL, the inner whether it was a value or an explicit "none". Rule G2
    // turns on that difference -- unset reads the environment, explicitly-empty must not.
    api_key: Option<Option<String>>,
    base_url: String,
    max_retries: u32,
    timeout: Option<Duration>,
    user_agent_suffix: Option<String>,
    transport: Option<Arc<dyn Transport>>,
    sleeper: Arc<dyn Sleeper>,
    jitter: bool,
    env: EnvLookup,
}

/// How the builder reads `LABELZOOM_API_KEY`. Injectable so a test can prove the client
/// did *not* consult the environment, which is the half of rule G2 that setting a real
/// environment variable cannot reach.
type EnvLookup = Arc<dyn Fn(&str) -> Option<String> + Send + Sync>;

impl Default for ClientBuilder {
    fn default() -> Self {
        Self {
            api_key: None,
            base_url: DEFAULT_BASE_URL.to_owned(),
            max_retries: 2,
            timeout: None,
            user_agent_suffix: None,
            transport: None,
            sleeper: Arc::new(ThreadSleeper),
            jitter: true,
            env: Arc::new(|key| std::env::var(key).ok()),
        }
    }
}

impl ClientBuilder {
    /// A builder with the defaults.
    #[must_use]
    pub fn new() -> Self {
        Self::default()
    }

    /// Sets the credential: an `lz_live_`/`lz_test_` key or a JWT.
    ///
    /// Passing an empty string forces anonymous mode and suppresses the
    /// `LABELZOOM_API_KEY` fallback, which is what you want when a config file may or may
    /// not carry a key and you do not want the environment deciding.
    #[must_use]
    pub fn api_key(mut self, api_key: impl Into<String>) -> Self {
        let api_key = api_key.into();
        self.api_key = Some(if api_key.is_empty() {
            None
        } else {
            Some(api_key)
        });
        self
    }

    /// Forces the free tier: no credential, and no environment fallback.
    #[must_use]
    pub fn anonymous(mut self) -> Self {
        self.api_key = Some(None);
        self
    }

    /// Overrides the API host. A path prefix is preserved, so a reverse proxy at
    /// `https://proxy.example.com/labelzoom` works.
    #[must_use]
    pub fn base_url(mut self, base_url: impl Into<String>) -> Self {
        self.base_url = base_url.into();
        self
    }

    /// Sets the number of retries after the initial attempt. Defaults to 2, for 3 attempts
    /// in total. Zero disables retrying.
    #[must_use]
    pub fn max_retries(mut self, max_retries: u32) -> Self {
        self.max_retries = max_retries;
        self
    }

    /// Sets a per-request timeout. Only honoured by the bundled transport.
    #[must_use]
    pub fn timeout(mut self, timeout: Duration) -> Self {
        self.timeout = Some(timeout);
        self
    }

    /// Appends a token to the SDK's own User-Agent.
    ///
    /// Appended, never prepended: the server parses a leading `LabelZoomStudio/` as a
    /// Studio version and silently changes PDF handling for versions <= 1.8.2.
    #[must_use]
    pub fn user_agent_suffix(mut self, suffix: impl Into<String>) -> Self {
        self.user_agent_suffix = Some(suffix.into());
        self
    }

    /// Substitutes the HTTP backend. This is the seam to stub in tests -- give it a
    /// [`Transport`] that returns canned responses and no socket is ever opened.
    #[must_use]
    pub fn transport(mut self, transport: Arc<dyn Transport>) -> Self {
        self.transport = Some(transport);
        self
    }

    /// Replaces the delay between retries. Substitute a recording no-op in tests so the
    /// retry paths cost no wall-clock time.
    #[must_use]
    pub fn sleeper(mut self, sleeper: Arc<dyn Sleeper>) -> Self {
        self.sleeper = sleeper;
        self
    }

    /// Turns retry jitter on or off. Off makes the backoff exactly 1s, 2s, 4s; leave it on
    /// in production, where it is what stops a fleet retrying in lockstep.
    #[must_use]
    pub fn jitter(mut self, jitter: bool) -> Self {
        self.jitter = jitter;
        self
    }

    /// Replaces the environment lookup used to find `LABELZOOM_API_KEY`. Injecting it
    /// keeps a developer's real key out of a test's outcome.
    #[must_use]
    pub fn env_lookup(
        mut self,
        lookup: impl Fn(&str) -> Option<String> + Send + Sync + 'static,
    ) -> Self {
        self.env = Arc::new(lookup);
        self
    }

    /// Builds the client.
    ///
    /// # Errors
    ///
    /// Returns [`Error::Validation`] when no transport is available -- which can only
    /// happen with `default-features = false` and no [`transport`](Self::transport) set.
    pub fn build(self) -> Result<LabelZoomClient, Error> {
        let transport = match self.transport {
            Some(transport) => transport,
            #[cfg(feature = "ureq-transport")]
            None => match self.timeout {
                Some(timeout) => Arc::new(crate::transport::UreqTransport::with_timeout(timeout)),
                None => Arc::new(crate::transport::UreqTransport::new()),
            },
            #[cfg(not(feature = "ureq-transport"))]
            None => {
                return Err(Error::validation(
                    "transport",
                    "This build has no bundled HTTP backend (the `ureq-transport` feature \
                     is off), so a Transport must be supplied with ClientBuilder::transport.",
                ))
            }
        };

        let credential = match self.api_key {
            // Never configured: read the environment.
            None => (self.env)(API_KEY_ENV_VAR).filter(|value| !value.is_empty()),
            // Explicitly configured, including explicitly empty. Must not consult the env.
            Some(api_key) => api_key,
        };

        let mut user_agent = format!(
            "labelzoom-rust-sdk/{VERSION} (rust; {} {})",
            std::env::consts::OS,
            std::env::consts::ARCH
        );
        if let Some(suffix) = self.user_agent_suffix.as_deref().map(str::trim) {
            if !suffix.is_empty() {
                user_agent.push(' ');
                user_agent.push_str(suffix);
            }
        }

        Ok(LabelZoomClient {
            // All trailing slashes, not one: a base URL of "https://api.labelzoom.com///"
            // must still produce a single-slash path.
            base_url: self.base_url.trim_end_matches('/').to_owned(),
            credential,
            max_retries: self.max_retries,
            user_agent,
            transport,
            sleeper: self.sleeper,
            jitter: self.jitter,
        })
    }
}

impl LabelZoomClient {
    /// A client with the defaults: `LABELZOOM_API_KEY` if set, anonymous otherwise.
    ///
    /// # Panics
    ///
    /// Only when the crate is built with `default-features = false`, where there is no
    /// bundled HTTP backend to fall back on. Use [`LabelZoomClient::builder`] there.
    #[must_use]
    pub fn new() -> Self {
        Self::builder()
            .build()
            .expect("the default build always has a transport")
    }

    /// Starts configuring a client.
    pub fn builder() -> ClientBuilder {
        ClientBuilder::new()
    }

    /// Whether a credential was resolved. `false` means requests go out on the anonymous
    /// free tier, which is a supported mode rather than an error.
    pub fn is_authenticated(&self) -> bool {
        self.credential.is_some()
    }

    /// The URL a given conversion would be posted to.
    ///
    /// # Errors
    ///
    /// Returns [`Error::Validation`] if the options are invalid.
    pub fn request_url(&self, request: &ConvertRequest) -> Result<String, Error> {
        // Concatenated, not resolved: a base URL carrying a path prefix
        // (https://proxy.example.com/labelzoom) must keep it, which a URL join would
        // discard.
        let mut url = format!(
            "{}/api/v2/convert/{}/to/{}",
            self.base_url,
            request.source.wire_token(),
            request.target.wire_token()
        );
        if let Some(params) = request.options.serialize()? {
            url.push_str("?params=");
            url.push_str(&percent_encode(&params));
        }
        Ok(url)
    }

    /// Runs one conversion.
    ///
    /// # Errors
    ///
    /// [`Error::Validation`] for a request rejected before it left the process,
    /// [`Error::Api`] for any non-2xx response, [`Error::Transport`] when no response
    /// arrived at all.
    pub fn convert(&self, request: &ConvertRequest) -> Result<ConversionResult, Error> {
        if request.body.is_empty() {
            // The gateway rejects a zero-length body with 400; catching it here saves a
            // round trip and gives a clearer message.
            return Err(Error::validation(
                "body",
                "Source body cannot be empty; the API rejects zero-length requests.",
            ));
        }

        let url = self.request_url(request)?;
        let content_type = if request.base64_text {
            "text/plain"
        } else {
            request.source.media_type()
        };

        let mut headers = vec![
            ("content-type".to_owned(), content_type.to_owned()),
            // Accept must be */*. The server's `produces` list omits image/gif, image/bmp
            // and image/jpeg, so naming the target's exact media type yields a 406 from
            // content negotiation before the handler ever runs.
            ("accept".to_owned(), "*/*".to_owned()),
            ("user-agent".to_owned(), self.user_agent.clone()),
        ];
        if let Some(credential) = &self.credential {
            headers.push(("authorization".to_owned(), format!("Bearer {credential}")));
        }

        let attempts = self.max_retries + 1;
        for attempt in 1..=attempts {
            let outgoing = HttpRequest {
                method: "POST",
                url: url.clone(),
                headers: headers.clone(),
                body: request.body.clone(),
            };

            let response = match self.transport.execute(outgoing) {
                Ok(response) => response,
                Err(error) => {
                    if attempt >= attempts {
                        return Err(Error::Transport(error));
                    }
                    self.delay(attempt, None);
                    continue;
                }
            };

            if (200..300).contains(&response.status) {
                return Ok(read_result(response));
            }

            let retry_after = retry_after_seconds(&response);
            if attempt >= attempts || !is_retryable(response.status) {
                return Err(Error::Api(read_error(&response, retry_after)));
            }
            self.delay(attempt, retry_after);
        }

        // The loop always returns: attempts is at least 1.
        Err(Error::Transport(TransportError::new(
            "the retry loop exited without a result",
        )))
    }

    /// Waits 1s, 2s, 4s with full jitter, overridden by a longer `Retry-After`.
    fn delay(&self, attempt: u32, retry_after: Option<f64>) {
        // attempt is bounded by max_retries + 1, so the exponent cannot overflow i32.
        let backoff = 2f64.powi(i32::try_from(attempt).unwrap_or(i32::MAX).saturating_sub(1));
        let mut wait = if self.jitter {
            backoff * pseudo_random()
        } else {
            backoff
        };

        // The server knows better than the backoff curve when it tells us how long to wait.
        if let Some(seconds) = retry_after {
            if seconds > wait {
                wait = seconds;
            }
        }

        self.sleeper.sleep(wait);
    }
}

impl Default for LabelZoomClient {
    fn default() -> Self {
        Self::new()
    }
}

fn is_retryable(status: u16) -> bool {
    status == 429 || status >= 500
}

fn read_result(response: HttpResponse) -> ConversionResult {
    ConversionResult {
        content_type: response.header("content-type").map(str::to_owned),
        // Case-insensitive lookup, which matters here: the gateway sets X-LZ-Request-Id
        // but CORS exposes it as X-LZ-Request-ID.
        request_id: response.header(REQUEST_ID_HEADER).map(str::to_owned),
        status: response.status,
        bytes: response.body,
    }
}

fn read_error(response: &HttpResponse, retry_after: Option<f64>) -> crate::error::ApiError {
    let request_id = response.header(REQUEST_ID_HEADER).map(str::to_owned);
    let raw_body = String::from_utf8_lossy(&response.body).into_owned();
    let message = extract_message(&raw_body, reason_phrase(response.status));

    error_for_status(response.status, message, request_id, raw_body, retry_after)
}

/// Reads `Retry-After` from the RESPONSE, on any retryable status.
///
/// Deliberately not read off the typed rate-limit error: RFC 9110 allows Retry-After on a
/// 503 too, and a 503 asking for 5 seconds must not be retried after a 1-second backoff.
///
/// The HTTP-date form is legal but rare here; treating it as absent falls back to the
/// backoff curve, which is safe. Same choice the Java SDK documents.
fn retry_after_seconds(response: &HttpResponse) -> Option<f64> {
    response.header("retry-after")?.trim().parse::<f64>().ok()
}

/// Percent-encodes a query parameter value.
///
/// Hand-rolled rather than pulling in a crate for it: the unreserved set is four lines,
/// and every conformance runner compares the decoded JSON structurally anyway, because
/// percent-encoding style differs per language.
fn percent_encode(value: &str) -> String {
    let mut encoded = String::with_capacity(value.len());
    for byte in value.bytes() {
        match byte {
            b'A'..=b'Z' | b'a'..=b'z' | b'0'..=b'9' | b'-' | b'_' | b'.' | b'~' => {
                encoded.push(byte as char);
            }
            other => {
                use std::fmt::Write as _;
                let _ = write!(encoded, "%{other:02X}");
            }
        }
    }
    encoded
}

/// Full jitter needs a random multiplier, not a cryptographic one.
///
/// Derived from the clock rather than taking a dependency on `rand`: this picks a delay,
/// and the only property that matters is that two processes retrying at the same moment
/// pick different ones.
fn pseudo_random() -> f64 {
    use std::time::{SystemTime, UNIX_EPOCH};

    let nanos = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map_or(0, |elapsed| elapsed.subsec_nanos());
    // Mixed so that successive calls a few microseconds apart do not land next to each
    // other on the unit interval. The modulus keeps the result well inside f64's exact
    // integer range, so the cast is lossless.
    let mixed = u64::from(nanos).wrapping_mul(6_364_136_223_846_793_005) >> 11;
    f64::from(u32::try_from(mixed % 1_000_000).unwrap_or(0)) / 1_000_000.0
}
