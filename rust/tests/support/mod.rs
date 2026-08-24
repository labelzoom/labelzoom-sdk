//! Fixture loading and the recording stubs the conformance harness runs on.

use labelzoom::{HttpRequest, HttpResponse, Sleeper, Transport, TransportError};
use serde_json::Value;
use std::path::{Path, PathBuf};
use std::sync::{Arc, Mutex};

pub const LANGUAGE: &str = "rust";

/// Walks up from the test binary's manifest directory looking for `conformance/cases`.
pub fn conformance_root() -> PathBuf {
    let mut directory: &Path = Path::new(env!("CARGO_MANIFEST_DIR"));
    loop {
        let candidate = directory.join("conformance");
        if candidate.join("cases").is_dir() {
            return candidate;
        }
        directory = directory
            .parent()
            .expect("could not locate the conformance/ directory");
    }
}

pub fn read_json(path: &Path) -> Value {
    let raw = std::fs::read_to_string(path)
        .unwrap_or_else(|error| panic!("could not read {}: {error}", path.display()));
    serde_json::from_str(&raw)
        .unwrap_or_else(|error| panic!("could not parse {}: {error}", path.display()))
}

/// Replays a scripted list of responses and records every request it was sent.
///
/// Implementing the crate's own `Transport` trait rather than intercepting at a lower
/// level is what makes "the suite never opens a socket" structural: with
/// `--no-default-features` there is no HTTP stack linked in at all.
pub struct RecordingTransport {
    script: Mutex<std::vec::IntoIter<Value>>,
    pub requests: Mutex<Vec<HttpRequest>>,
}

impl RecordingTransport {
    pub fn new(script: Vec<Value>) -> Arc<Self> {
        Arc::new(Self {
            script: Mutex::new(script.into_iter()),
            requests: Mutex::new(Vec::new()),
        })
    }

    pub fn count(&self) -> usize {
        self.requests.lock().unwrap().len()
    }

    pub fn last(&self) -> Option<HttpRequest> {
        self.requests.lock().unwrap().last().cloned()
    }
}

impl Transport for RecordingTransport {
    fn execute(&self, request: HttpRequest) -> Result<HttpResponse, TransportError> {
        self.requests.lock().unwrap().push(request.clone());

        let scripted = self.script.lock().unwrap().next().unwrap_or_else(|| {
            panic!(
                "unexpected request to {}; no more responses are scripted",
                request.url
            )
        });

        if scripted.get("transportError").is_some() {
            return Err(TransportError::new("connection refused"));
        }

        let status = u16::try_from(
            scripted
                .get("status")
                .and_then(Value::as_u64)
                .expect("a scripted response needs a status"),
        )
        .expect("status out of range");

        let headers = scripted
            .get("headers")
            .and_then(Value::as_object)
            .map(|map| {
                map.iter()
                    .map(|(name, value)| {
                        (name.clone(), value.as_str().unwrap_or_default().to_owned())
                    })
                    .collect()
            })
            .unwrap_or_default();

        Ok(HttpResponse {
            status,
            headers,
            body: scripted_body(&scripted),
        })
    }
}

fn scripted_body(scripted: &Value) -> Vec<u8> {
    if let Some(encoded) = scripted.get("bodyBase64").and_then(Value::as_str) {
        return base64_decode(encoded);
    }
    if let Some(repeat) = scripted.get("bodyTextRepeat").and_then(Value::as_array) {
        let fill = repeat[0].as_str().unwrap_or_default();
        let count = usize::try_from(repeat[1].as_u64().unwrap_or(0)).unwrap_or(0);
        return fill.repeat(count).into_bytes();
    }
    scripted
        .get("bodyText")
        .and_then(Value::as_str)
        .unwrap_or_default()
        .as_bytes()
        .to_vec()
}

/// Records what the retry backoff asked for instead of waiting.
///
/// Rule F4 makes this seam a requirement rather than a convenience: a suite that really
/// slept would cost ten seconds of CI time.
#[derive(Default)]
pub struct RecordingSleeper {
    pub slept: Mutex<Vec<f64>>,
}

impl RecordingSleeper {
    pub fn seconds(&self) -> Vec<f64> {
        self.slept.lock().unwrap().clone()
    }
}

impl Sleeper for RecordingSleeper {
    fn sleep(&self, seconds: f64) {
        self.slept.lock().unwrap().push(seconds);
    }
}

// base64 in ~20 lines rather than a dev-dependency: it is used for two fixture fields and
// the alphabet has not changed since 1987.
const BASE64_ALPHABET: &[u8; 64] =
    b"ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

pub fn base64_decode(encoded: &str) -> Vec<u8> {
    let mut out = Vec::new();
    let mut buffer = 0u32;
    let mut bits = 0u32;
    for byte in encoded
        .bytes()
        .filter(|b| !b.is_ascii_whitespace() && *b != b'=')
    {
        let value = BASE64_ALPHABET
            .iter()
            .position(|candidate| *candidate == byte)
            .unwrap_or_else(|| panic!("{} is not a base64 character", byte as char));
        buffer = (buffer << 6) | value as u32;
        bits += 6;
        if bits >= 8 {
            bits -= 8;
            out.push((buffer >> bits) as u8);
        }
    }
    out
}

pub fn base64_encode(bytes: &[u8]) -> String {
    let mut out = String::new();
    for chunk in bytes.chunks(3) {
        let mut block = [0u8; 3];
        block[..chunk.len()].copy_from_slice(chunk);
        let packed = (u32::from(block[0]) << 16) | (u32::from(block[1]) << 8) | u32::from(block[2]);
        for index in 0..4 {
            if index <= chunk.len() {
                out.push(BASE64_ALPHABET[((packed >> (18 - 6 * index)) & 0x3F) as usize] as char);
            } else {
                out.push('=');
            }
        }
    }
    out
}
