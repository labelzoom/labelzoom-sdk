//! Source and target formats, and the one place a format's media type is written down.

use std::fmt;

/// The format of the document being converted.
///
/// A distinct type from [`TargetFormat`], and the two sets are not the same: [`Jpg`] and
/// [`Url`] are source-only. `Jpg` is an input spelling that normalizes to `jpeg` on the
/// wire, and `Url` is an instruction to the server to go fetch a document rather than a
/// format at all.
///
/// [`Jpg`]: SourceFormat::Jpg
/// [`Url`]: SourceFormat::Url
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash)]
#[non_exhaustive]
pub enum SourceFormat {
    /// Zebra Programming Language.
    Zpl,
    /// Eltron Programming Language.
    Epl,
    /// `TSC` Printer Language.
    Tspl,
    /// Datamax Programming Language.
    Dpl,
    /// The LabelZoom label model, as XML.
    Xml,
    /// The LabelZoom label model, as JSON.
    Json,
    /// A PDF document.
    Pdf,
    /// A PNG image.
    Png,
    /// A BMP image.
    Bmp,
    /// A GIF image.
    Gif,
    /// A JPEG image.
    Jpeg,
    /// An alias for [`SourceFormat::Jpeg`]; sent as `jpeg`.
    Jpg,
    /// Has the *server* fetch a URL and convert what it finds. The request body is the URL.
    ///
    /// Validate the URL first if it came from untrusted input: the fetch happens
    /// server-side, from LabelZoom's network, not from yours.
    Url,
}

/// The format to convert into.
///
/// There is no `TargetFormat::Url`: a URL is a fetch instruction, not an output format.
/// There is no `Jpg` either -- that spelling is source-side only.
///
/// The printer languages round-trip: `Epl`, `Tspl` and `Dpl` are targets as well as
/// sources, as of contract 1.1.0.
///
/// Marked `#[non_exhaustive]` on purpose. This set has already grown once -- the three
/// printer languages became targets when the printer-language writers shipped -- and
/// without it every such addition is a breaking change for any downstream `match`.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash)]
#[non_exhaustive]
pub enum TargetFormat {
    /// Zebra Programming Language.
    Zpl,
    /// Eltron Programming Language. Can inline raw binary (`GW`); prefer
    /// [`ConversionResult::bytes`](crate::ConversionResult::bytes) over `text()`.
    Epl,
    /// TSC Printer Language. Can inline raw binary (`BITMAP`); prefer
    /// [`ConversionResult::bytes`](crate::ConversionResult::bytes) over `text()`.
    Tspl,
    /// Datamax Programming Language.
    Dpl,
    /// The LabelZoom label model, as XML.
    Xml,
    /// The LabelZoom label model, as JSON.
    Json,
    /// A PDF document.
    Pdf,
    /// A PNG image.
    Png,
    /// A BMP image.
    Bmp,
    /// A GIF image.
    Gif,
    /// A JPEG image.
    Jpeg,
}

impl SourceFormat {
    /// Every accepted source, in the contract's order.
    pub const ALL: &'static [SourceFormat] = &[
        Self::Zpl,
        Self::Epl,
        Self::Tspl,
        Self::Dpl,
        Self::Xml,
        Self::Json,
        Self::Pdf,
        Self::Png,
        Self::Bmp,
        Self::Gif,
        Self::Jpeg,
        Self::Jpg,
        Self::Url,
    ];

    /// The path segment for this source. `Jpg` normalizes to `jpeg` (rule A2).
    pub fn wire_token(self) -> &'static str {
        match self {
            Self::Jpg => "jpeg",
            other => other.name(),
        }
    }

    /// The request `Content-Type` for this source.
    ///
    /// The format metadata table, defined once and never inlined at a call site. The
    /// superseded .NET design had a builder type per format, each independently knowing
    /// this mapping, and they drifted -- one of them emitted the source type as the target.
    pub fn media_type(self) -> &'static str {
        match self {
            // The body for Url is the URL itself.
            Self::Zpl | Self::Epl | Self::Tspl | Self::Dpl | Self::Url => "text/plain",
            Self::Xml => "application/xml",
            Self::Json => "application/json",
            Self::Pdf => "application/pdf",
            Self::Png => "image/png",
            Self::Bmp => "image/bmp",
            Self::Gif => "image/gif",
            Self::Jpeg | Self::Jpg => "image/jpeg",
        }
    }

    /// The lowercase name of this format, before `jpg` normalization.
    pub fn name(self) -> &'static str {
        match self {
            Self::Zpl => "zpl",
            Self::Epl => "epl",
            Self::Tspl => "tspl",
            Self::Dpl => "dpl",
            Self::Xml => "xml",
            Self::Json => "json",
            Self::Pdf => "pdf",
            Self::Png => "png",
            Self::Bmp => "bmp",
            Self::Gif => "gif",
            Self::Jpeg => "jpeg",
            Self::Jpg => "jpg",
            Self::Url => "url",
        }
    }

    /// Parses a wire token, for deserializing configuration.
    pub fn from_name(name: &str) -> Option<Self> {
        Self::ALL
            .iter()
            .copied()
            .find(|format| format.name() == name.to_ascii_lowercase())
    }
}

impl TargetFormat {
    /// Every accepted target, in the contract's order.
    pub const ALL: &'static [TargetFormat] = &[
        Self::Zpl,
        Self::Epl,
        Self::Tspl,
        Self::Dpl,
        Self::Xml,
        Self::Json,
        Self::Pdf,
        Self::Png,
        Self::Bmp,
        Self::Gif,
        Self::Jpeg,
    ];

    /// The path segment for this target. Targets need no normalization.
    pub fn wire_token(self) -> &'static str {
        match self {
            Self::Zpl => "zpl",
            Self::Epl => "epl",
            Self::Tspl => "tspl",
            Self::Dpl => "dpl",
            Self::Xml => "xml",
            Self::Json => "json",
            Self::Pdf => "pdf",
            Self::Png => "png",
            Self::Bmp => "bmp",
            Self::Gif => "gif",
            Self::Jpeg => "jpeg",
        }
    }

    /// Parses a wire token, for deserializing configuration.
    pub fn from_name(name: &str) -> Option<Self> {
        Self::ALL
            .iter()
            .copied()
            .find(|format| format.wire_token() == name.to_ascii_lowercase())
    }
}

impl fmt::Display for SourceFormat {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str(self.name())
    }
}

impl fmt::Display for TargetFormat {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str(self.wire_token())
    }
}
