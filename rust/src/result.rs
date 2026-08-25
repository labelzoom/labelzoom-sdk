//! The success type.

use std::borrow::Cow;
use std::path::Path;

/// The outcome of a successful conversion.
///
/// [`bytes`](ConversionResult::bytes) is authoritative. PDF, PNG, BMP, GIF and JPEG
/// targets are binary, so treating the response as text would silently corrupt five of the
/// eleven targets -- and EPL and TSPL reach the same hazard through a `text/plain`
/// response, because their `GW` and `BITMAP` commands inline a raw 1-bpp payload.
#[derive(Clone, Debug)]
pub struct ConversionResult {
    /// The response body, exactly as the server sent it.
    pub bytes: Vec<u8>,
    /// The response `Content-Type` header, if any.
    pub content_type: Option<String>,
    /// The HTTP status code, always 2xx here.
    pub status: u16,
    /// The `X-LZ-Request-Id` response header, when the server sent one. The support handle.
    pub request_id: Option<String>,
}

impl ConversionResult {
    /// Decodes [`bytes`](ConversionResult::bytes) using the response charset, defaulting
    /// to UTF-8.
    ///
    /// Borrowed when the bytes are already valid UTF-8, so calling this on a large payload
    /// costs nothing until a transcode is actually needed.
    ///
    /// Safe for the textual targets. For a binary target, or an EPL/TSPL label that might
    /// carry graphics, read the bytes instead.
    pub fn text(&self) -> Cow<'_, str> {
        decode(&self.bytes, self.content_type.as_deref())
    }

    /// Writes the bytes to `path`.
    pub fn save(&self, path: impl AsRef<Path>) -> std::io::Result<()> {
        std::fs::write(path, &self.bytes)
    }
}

/// Decodes `body` with the charset named in `content_type`.
///
/// Not a hardcoded UTF-8 decode: the API serves ISO-8859-1 for some conversions, and a
/// byte like 0xE9 is not valid UTF-8 -- it would come back as U+FFFD rather than "é".
fn decode<'a>(body: &'a [u8], content_type: Option<&str>) -> Cow<'a, str> {
    let encoding = charset_of(content_type)
        .and_then(|charset| encoding_rs::Encoding::for_label(charset.as_bytes()))
        .unwrap_or(encoding_rs::UTF_8);

    let (decoded, _, _) = encoding.decode(body);
    decoded
}

/// Pulls the charset parameter off a Content-Type header.
fn charset_of(content_type: Option<&str>) -> Option<String> {
    let header = content_type?;
    header.split(';').skip(1).find_map(|parameter| {
        let (name, value) = parameter.split_once('=')?;
        if !name.trim().eq_ignore_ascii_case("charset") {
            return None;
        }
        Some(value.trim().trim_matches('"').to_owned())
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn decodes_latin1_by_the_response_charset() {
        // The exact bytes response/success-text-non-utf8-charset sends: "Hello " + 0xE9,
        // which is invalid UTF-8 and would otherwise decode to U+FFFD.
        let body = b"Hello \xE9";
        assert_eq!(
            decode(body, Some("text/plain;charset=ISO-8859-1")),
            "Hello é"
        );
        assert_eq!(decode(body, Some("text/plain")), "Hello \u{FFFD}");
    }

    #[test]
    fn ignores_an_unrecognized_charset() {
        assert_eq!(
            decode(b"hi", Some("text/plain; charset=\"not-a-charset\"")),
            "hi"
        );
        assert_eq!(decode(b"hi", None), "hi");
    }

    #[test]
    fn strips_quotes_around_the_charset() {
        assert_eq!(
            charset_of(Some("text/plain; charset=\"utf-8\"")).as_deref(),
            Some("utf-8")
        );
    }
}
