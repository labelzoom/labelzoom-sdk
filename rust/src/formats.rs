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
    /// Intermec Printer Language.
    Ipl,
    /// `TSC` Printer Language.
    Tspl,
    /// Datamax Programming Language.
    Dpl,
    /// SATO Barcode Printer Language.
    Sbpl,
    /// Epson `ColorWorks` ESC/Label, a dialect of ZPL.
    EscLabel,
    /// Honeywell/Intermec Fingerprint and Direct Protocol.
    Fingerprint,
    /// `GoDEX` EZPL. Not an EPL dialect.
    Ezpl,
    /// Zebra CPCL, for Zebra's mobile printers.
    Cpcl,
    /// Toshiba TEC TPCL.
    Tpcl,
    /// A `NiceLabel` or `ZebraDesigner` 3 `.nlbl` design file. Send its bytes.
    Nlbl,
    /// A `NiceLabel` `.lbl` design file. Send its bytes.
    Lbl,
    /// A `BarTender` `.btw` design file. Send its bytes.
    Btw,
    /// A DYMO Label `.label` or `.dymo` design file (XML).
    Dymo,
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
/// Most printer languages round-trip: `Epl`, `Ipl`, `Tspl`, `Dpl`, `Sbpl` and `Cpcl` are
/// targets as well as sources.
///
/// Marked `#[non_exhaustive]` on purpose. This set has already grown three times -- three
/// printer languages became targets when the printer-language writers shipped, then `Ipl` and
/// `Sbpl` arrived with their adapters, then `Cpcl` -- and without it every such addition is a breaking change
/// for any downstream `match`.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash)]
#[non_exhaustive]
pub enum TargetFormat {
    /// Zebra Programming Language.
    Zpl,
    /// Eltron Programming Language. Can inline raw binary (`GW`); prefer
    /// [`ConversionResult::bytes`](crate::ConversionResult::bytes) over `text()`.
    Epl,
    /// Intermec Printer Language. Frames commands in `STX`/`ETX` and can carry packed
    /// bitmap columns; prefer [`ConversionResult::bytes`](crate::ConversionResult::bytes)
    /// over `text()`.
    Ipl,
    /// TSC Printer Language. Can inline raw binary (`BITMAP`); prefer
    /// [`ConversionResult::bytes`](crate::ConversionResult::bytes) over `text()`.
    Tspl,
    /// Datamax Programming Language.
    Dpl,
    /// SATO Barcode Printer Language. Frames every command with `ESC` and can embed
    /// inline graphics; prefer [`ConversionResult::bytes`](crate::ConversionResult::bytes)
    /// over `text()`.
    Sbpl,
    /// Zebra CPCL. Graphics are written as hex, so the output is plain text.
    Cpcl,
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
        Self::Ipl,
        Self::Tspl,
        Self::Dpl,
        Self::Sbpl,
        Self::EscLabel,
        Self::Fingerprint,
        Self::Ezpl,
        Self::Cpcl,
        Self::Tpcl,
        Self::Nlbl,
        Self::Lbl,
        Self::Btw,
        Self::Dymo,
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
            Self::Zpl
            | Self::Epl
            | Self::Ipl
            | Self::Tspl
            | Self::Dpl
            | Self::Sbpl
            | Self::EscLabel
            | Self::Fingerprint
            | Self::Ezpl
            | Self::Cpcl
            | Self::Tpcl
            | Self::Url => "text/plain",
            Self::Nlbl | Self::Lbl | Self::Btw => "application/octet-stream",
            Self::Xml | Self::Dymo => "application/xml",
            Self::Json => "application/json",
            Self::Pdf => "application/pdf",
            Self::Png => "image/png",
            Self::Bmp => "image/bmp",
            Self::Gif => "image/gif",
            Self::Jpeg | Self::Jpg => "image/jpeg",
        }
    }

    /// Whether [`LabelZoomClient::print`](crate::LabelZoomClient::print) accepts this source.
    ///
    /// Every source format prints except [`Url`](Self::Url): LabelZoom converts the document
    /// to the printer's configured language first, but print doesn't fetch, so a URL has no
    /// document to convert. Fetch it yourself and print the bytes as their own format.
    pub fn is_printable(self) -> bool {
        !matches!(self, Self::Url)
    }

    /// The lowercase name of this format, before `jpg` normalization.
    pub fn name(self) -> &'static str {
        match self {
            Self::Zpl => "zpl",
            Self::Epl => "epl",
            Self::Ipl => "ipl",
            Self::Tspl => "tspl",
            Self::Dpl => "dpl",
            Self::Sbpl => "sbpl",
            Self::EscLabel => "esclabel",
            Self::Fingerprint => "fingerprint",
            Self::Ezpl => "ezpl",
            Self::Cpcl => "cpcl",
            Self::Tpcl => "tpcl",
            Self::Nlbl => "nlbl",
            Self::Lbl => "lbl",
            Self::Btw => "btw",
            Self::Dymo => "dymo",
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
        Self::Ipl,
        Self::Tspl,
        Self::Dpl,
        Self::Sbpl,
        Self::Cpcl,
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
            Self::Ipl => "ipl",
            Self::Tspl => "tspl",
            Self::Dpl => "dpl",
            Self::Sbpl => "sbpl",
            Self::Cpcl => "cpcl",
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
