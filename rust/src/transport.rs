//! The HTTP seam.
//!
//! The client talks to a [`Transport`] rather than to a concrete HTTP client, so a test
//! can answer requests without opening a socket and an embedder can route through an HTTP
//! stack they already have. Rule F4 requires the same of the sleeper.

use crate::error::TransportError;

/// One outgoing HTTP request.
#[derive(Debug, Clone)]
pub struct HttpRequest {
    /// Always `POST` today.
    pub method: &'static str,
    /// The fully resolved URL, query string included.
    pub url: String,
    /// Header name/value pairs, in the order the client set them.
    pub headers: Vec<(String, String)>,
    /// The request body.
    pub body: Vec<u8>,
}

impl HttpRequest {
    /// The first header matching `name`, compared case-insensitively.
    pub fn header(&self, name: &str) -> Option<&str> {
        self.headers
            .iter()
            .find(|(key, _)| key.eq_ignore_ascii_case(name))
            .map(|(_, value)| value.as_str())
    }
}

/// One HTTP response, including a non-2xx one.
///
/// A transport must return a response for any status the server produced; only a failure
/// to *get* a response is a [`TransportError`].
#[derive(Debug, Clone)]
pub struct HttpResponse {
    /// The HTTP status code.
    pub status: u16,
    /// Header name/value pairs.
    pub headers: Vec<(String, String)>,
    /// The response body.
    pub body: Vec<u8>,
}

impl HttpResponse {
    /// The first header matching `name`, compared case-insensitively.
    ///
    /// Case-insensitive because it has to be: the gateway sets `X-LZ-Request-Id` but CORS
    /// exposes it as `X-LZ-Request-ID`.
    pub fn header(&self, name: &str) -> Option<&str> {
        self.headers
            .iter()
            .find(|(key, _)| key.eq_ignore_ascii_case(name))
            .map(|(_, value)| value.as_str())
    }
}

/// Executes HTTP requests on the client's behalf.
///
/// Implement this to stub the network in tests, or to route through your own HTTP stack:
///
/// ```
/// use labelzoom::{HttpRequest, HttpResponse, Transport, TransportError};
///
/// struct AlwaysOk;
///
/// impl Transport for AlwaysOk {
///     fn execute(&self, _request: HttpRequest) -> Result<HttpResponse, TransportError> {
///         Ok(HttpResponse {
///             status: 200,
///             headers: vec![("content-type".into(), "text/plain".into())],
///             body: b"^XA^XZ".to_vec(),
///         })
///     }
/// }
/// ```
pub trait Transport: Send + Sync {
    /// Sends one request and returns the response, whatever its status.
    fn execute(&self, request: HttpRequest) -> Result<HttpResponse, TransportError>;
}

/// Waits between retry attempts.
///
/// A hard design requirement, not a convenience: a conformance suite that really slept
/// would cost ten seconds of CI time, so the fixtures assert on what the client *asked*
/// to wait rather than on elapsed time.
pub trait Sleeper: Send + Sync {
    /// Sleeps for `seconds`.
    fn sleep(&self, seconds: f64);
}

/// Sleeps for real. The default.
#[derive(Debug, Clone, Copy, Default)]
pub struct ThreadSleeper;

impl Sleeper for ThreadSleeper {
    fn sleep(&self, seconds: f64) {
        if seconds > 0.0 {
            std::thread::sleep(std::time::Duration::from_secs_f64(seconds));
        }
    }
}

#[cfg(feature = "ureq-transport")]
mod ureq_transport {
    use super::{HttpRequest, HttpResponse, Transport};
    use crate::error::TransportError;
    use std::time::Duration;

    /// The bundled blocking backend, behind the default `ureq-transport` feature.
    ///
    /// `ureq` rather than an async client: this crate makes one request per call, and a
    /// full async runtime is a large thing to require of a caller who only wants to
    /// convert a label. rustls rather than native-tls, so building the crate never needs
    /// OpenSSL headers.
    #[derive(Debug, Clone)]
    pub struct UreqTransport {
        timeout: Option<Duration>,
    }

    impl UreqTransport {
        /// A transport with no client-side timeout beyond `ureq`'s own defaults.
        pub fn new() -> Self {
            Self { timeout: None }
        }

        /// A transport that gives up on a request after `timeout`.
        pub fn with_timeout(timeout: Duration) -> Self {
            Self {
                timeout: Some(timeout),
            }
        }
    }

    impl Default for UreqTransport {
        fn default() -> Self {
            Self::new()
        }
    }

    impl Transport for UreqTransport {
        fn execute(&self, request: HttpRequest) -> Result<HttpResponse, TransportError> {
            // http_status_as_error(false) is load-bearing, not a preference: rule E1
            // requires a non-2xx body to reach the caller, and ureq's default turns a 404
            // into an Err whose body is far harder to get at.
            let mut config = ureq::Agent::config_builder().http_status_as_error(false);
            if let Some(timeout) = self.timeout {
                config = config.timeout_global(Some(timeout));
            }
            let agent: ureq::Agent = config.build().into();

            let mut outgoing = agent.post(&request.url);
            for (name, value) in &request.headers {
                outgoing = outgoing.header(name.as_str(), value.as_str());
            }

            // A non-2xx is a response, not a transport failure: rule E1 requires the body
            // to reach the caller rather than being swallowed by the HTTP client.
            let response = outgoing.send(&request.body[..]).map_err(|error| {
                TransportError::with_source(
                    format!("request to {} failed: {error}", request.url),
                    error,
                )
            })?;

            let status = response.status().as_u16();
            let headers = response
                .headers()
                .iter()
                .map(|(name, value)| {
                    (
                        name.as_str().to_owned(),
                        value.to_str().unwrap_or_default().to_owned(),
                    )
                })
                .collect();

            let body = response.into_body().read_to_vec().map_err(|error| {
                TransportError::new(format!("could not read the response: {error}"))
            })?;

            Ok(HttpResponse {
                status,
                headers,
                body,
            })
        }
    }
}

#[cfg(feature = "ureq-transport")]
pub use ureq_transport::UreqTransport;
