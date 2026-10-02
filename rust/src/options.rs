//! Conversion parameters and their `?params=<JSON>` serialization.

use crate::error::Error;
use crate::formats::{SourceFormat, TargetFormat};
use serde::Serialize;
use serde_json::{Map, Value};

/// How colour is reduced when rasterizing. Server default `GRAYSCALE`.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize)]
#[non_exhaustive]
pub enum ColorMode {
    /// Pure black and white.
    #[serde(rename = "BW")]
    Bw,
    /// Greyscale.
    #[serde(rename = "GRAYSCALE")]
    Grayscale,
    /// Full colour.
    #[serde(rename = "COLOR")]
    Color,
}

/// How a PDF source is interpreted. Server default `IMAGE`.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize)]
#[non_exhaustive]
pub enum PdfConversionMode {
    /// Rasterize the page.
    #[serde(rename = "IMAGE")]
    Image,
    /// Extract the page's text and vectors.
    #[serde(rename = "NATIVE")]
    Native,
}

/// Encoding of images embedded in ZPL output. Server default `Z64`.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize)]
#[non_exhaustive]
pub enum ZplImageCompression {
    /// Base-64 encoded, DEFLATE compressed.
    #[serde(rename = "Z64")]
    Z64,
    /// Run-length encoded hexadecimal.
    #[serde(rename = "COMPRESSED_HEX")]
    CompressedHex,
}

/// The pixel offset of the extracted region. Server default 0,0.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Serialize)]
pub struct Position {
    /// Horizontal offset, in pixels.
    pub x: i64,
    /// Vertical offset, in pixels.
    pub y: i64,
}

/// The media size, in INCHES -- not dots, and not millimetres.
///
/// Omitting it entirely is meaningful: it asks the server to detect the size. That is why
/// [`ConversionOptions::label`] is an `Option` and why an unset label emits no `label` key
/// at all.
#[derive(Clone, Copy, Debug, Default, PartialEq, Serialize)]
pub struct LabelSize {
    /// Width in inches.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub width: Option<f64>,
    /// Height in inches.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub height: Option<f64>,
}

/// How a PDF source is read.
#[derive(Clone, Copy, Debug, Default, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct PdfOptions {
    /// Rasterize or extract. Server default `IMAGE`.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub conversion_mode: Option<PdfConversionMode>,
    /// ZERO-BASED. Leave `None` to convert every page.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub page_number: Option<i64>,
}

/// ZPL output settings.
#[derive(Clone, Debug, Default, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ZplOptions {
    /// Drop the named commands from the output, e.g. `["^PQ"]`.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub commands_to_ignore: Option<Vec<String>>,
    /// Encoding of embedded images. Server default `Z64`.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub image_compression: Option<ZplImageCompression>,
}

/// The conversion parameters, in the shape the API expects.
///
/// Every field is an `Option` and **only the ones you set are sent** -- `skip_serializing_if`
/// is rule C1 expressed in the type system. The SDK never fills in a client-side default,
/// so a change to a server default reaches you without a crate upgrade. Setting nothing at
/// all produces a bare URL with no query string.
#[derive(Clone, Debug, Default, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ConversionOptions {
    /// How the source's absolute positions are interpreted: dots for printer languages, pixels
    /// for bitmap images, an override of the document's dpi for LabelZoom XML/JSON. Not
    /// applicable to PDF sources (vector).
    #[serde(skip_serializing_if = "Option::is_none")]
    pub source_dpi: Option<i64>,
    /// The resolution the output is authored at. Applies to printer-language and XML/JSON
    /// targets, and to raster targets when the source is a bitmap or PDF. Server default 203.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub target_dpi: Option<i64>,
    /// Legacy alias for a single print resolution; the server applies it to whichever side(s)
    /// of the chosen path support a dpi. Prefer `source_dpi` or `target_dpi`.
    ///
    /// Deprecated: use `source_dpi` or `target_dpi`.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub dpi: Option<i64>,
    /// Degrees clockwise. Must be a multiple of 90. Server default 0.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub rotation: Option<i64>,
    /// Percentage. Server default 100.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub scaling: Option<f64>,
    /// Colour reduction. Server default `GRAYSCALE`.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub color_mode: Option<ColorMode>,
    /// Luminance threshold from 0 to 100. Server default 70.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub darkness: Option<i64>,
    /// Pixel offset of the extracted region.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub position: Option<Position>,
    /// Forced on for the anonymous free tier regardless of what you set.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub watermark: Option<bool>,
    /// A printer dialect, e.g. `"moca"`. Requires a paid license.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub dialect: Option<String>,
    /// The variable-field values, one entry per output label.
    ///
    /// Typed as `Vec<Value>` rather than `Vec<Map<String, Value>>` so that a non-object
    /// element is *expressible* and can be rejected with a [`ValidationError`] -- which is
    /// what lets this crate run `validation/data-element-not-an-object` rather than
    /// declaring it away. Use [`ConversionOptions::with_data`] to build it from records.
    ///
    /// [`ValidationError`]: crate::ValidationError
    #[serde(skip_serializing_if = "Option::is_none")]
    pub data: Option<Vec<Value>>,
    /// The media size, in INCHES. Leave `None` to have the server detect it.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub label: Option<LabelSize>,
    /// How a PDF source is read.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub pdf: Option<PdfOptions>,
    /// ZPL output settings.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub zpl: Option<ZplOptions>,
    /// Anything this crate does not model yet. Unknown keys are ignored server-side, so
    /// this is a safe forward-compatibility escape hatch. Merged at the top level of the
    /// params object.
    #[serde(flatten)]
    pub extra: Map<String, Value>,
}

impl ConversionOptions {
    /// Fresh options with nothing set, which sends no query string at all.
    #[must_use]
    pub fn new() -> Self {
        Self::default()
    }

    /// Sets how the source's absolute positions are interpreted. Not applicable to PDF sources.
    #[must_use]
    pub fn source_dpi(mut self, dpi: i64) -> Self {
        self.source_dpi = Some(dpi);
        self
    }

    /// Sets the resolution the output is authored at.
    #[must_use]
    pub fn target_dpi(mut self, dpi: i64) -> Self {
        self.target_dpi = Some(dpi);
        self
    }

    /// Sets the legacy single print resolution. Prefer `source_dpi` or `target_dpi`.
    #[must_use]
    #[deprecated(since = "1.1.0", note = "use `source_dpi` or `target_dpi`")]
    pub fn dpi(mut self, dpi: i64) -> Self {
        self.dpi = Some(dpi);
        self
    }

    /// Sets the rotation, in degrees clockwise. Must be a multiple of 90.
    #[must_use]
    pub fn rotation(mut self, degrees: i64) -> Self {
        self.rotation = Some(degrees);
        self
    }

    /// Sets the scaling percentage.
    #[must_use]
    pub fn scaling(mut self, percent: f64) -> Self {
        self.scaling = Some(percent);
        self
    }

    /// Sets the colour mode.
    #[must_use]
    pub fn color_mode(mut self, mode: ColorMode) -> Self {
        self.color_mode = Some(mode);
        self
    }

    /// Sets the luminance threshold, 0 to 100.
    #[must_use]
    pub fn darkness(mut self, darkness: i64) -> Self {
        self.darkness = Some(darkness);
        self
    }

    /// Sets the pixel offset of the extracted region.
    #[must_use]
    pub fn position(mut self, x: i64, y: i64) -> Self {
        self.position = Some(Position { x, y });
        self
    }

    /// Turns the watermark on or off. An explicit `false` is sent.
    #[must_use]
    pub fn watermark(mut self, watermark: bool) -> Self {
        self.watermark = Some(watermark);
        self
    }

    /// Sets the printer dialect. Requires a paid license.
    #[must_use]
    pub fn dialect(mut self, dialect: impl Into<String>) -> Self {
        self.dialect = Some(dialect.into());
        self
    }

    /// Sets the media size, in **inches**.
    #[must_use]
    pub fn label_size(mut self, width_inches: f64, height_inches: f64) -> Self {
        self.label = Some(LabelSize {
            width: Some(width_inches),
            height: Some(height_inches),
        });
        self
    }

    /// Sets how a PDF source is read.
    #[must_use]
    pub fn pdf_conversion_mode(mut self, mode: PdfConversionMode) -> Self {
        self.pdf
            .get_or_insert_with(PdfOptions::default)
            .conversion_mode = Some(mode);
        self
    }

    /// Selects a single PDF page. **Zero-based**; omit to convert every page.
    #[must_use]
    pub fn pdf_page(mut self, zero_based_page_number: i64) -> Self {
        self.pdf.get_or_insert_with(PdfOptions::default).page_number = Some(zero_based_page_number);
        self
    }

    /// Drops the named commands from ZPL output.
    #[must_use]
    pub fn zpl_commands_to_ignore<I, S>(mut self, commands: I) -> Self
    where
        I: IntoIterator<Item = S>,
        S: Into<String>,
    {
        self.zpl
            .get_or_insert_with(ZplOptions::default)
            .commands_to_ignore = Some(commands.into_iter().map(Into::into).collect());
        self
    }

    /// Sets the encoding of images embedded in ZPL output.
    #[must_use]
    pub fn zpl_image_compression(mut self, compression: ZplImageCompression) -> Self {
        self.zpl
            .get_or_insert_with(ZplOptions::default)
            .image_compression = Some(compression);
        self
    }

    /// Sets the variable-field values, one record per output label.
    #[must_use]
    pub fn with_data<I>(mut self, records: I) -> Self
    where
        I: IntoIterator<Item = Map<String, Value>>,
    {
        self.data = Some(records.into_iter().map(Value::Object).collect());
        self
    }

    /// Adds a parameter this crate does not model. Unknown keys are ignored server-side.
    #[must_use]
    pub fn extra(mut self, key: impl Into<String>, value: impl Into<Value>) -> Self {
        self.extra.insert(key.into(), value.into());
        self
    }

    /// Renders the options as the `params` JSON value, validating locally on the way.
    ///
    /// Everything travels in one `?params=<JSON>` parameter, never dot-notation. The API
    /// accepts both and merges them, but dot-notation cannot express every parameter --
    /// `?data=[{}]` is rejected with 400 while `?params={"data":[{}]}` succeeds. One
    /// serialization path, no special cases.
    ///
    /// Returns `None` when nothing was set, in which case no query parameter is emitted.
    pub(crate) fn serialize(&self) -> Result<Option<String>, Error> {
        if let Some(rotation) = self.rotation {
            if rotation % 90 != 0 {
                // Rejected locally: the server would 400, and this is unambiguously a
                // caller bug.
                return Err(Error::validation(
                    "rotation",
                    format!("Rotation must be a multiple of 90 degrees, but was {rotation}."),
                ));
            }
        }
        if let Some(darkness) = self.darkness {
            if !(0..=100).contains(&darkness) {
                return Err(Error::validation(
                    "darkness",
                    format!("Darkness must be between 0 and 100, but was {darkness}."),
                ));
            }
        }
        if let Some(records) = &self.data {
            for (index, record) in records.iter().enumerate() {
                if !record.is_object() {
                    return Err(Error::validation(
                        "data",
                        format!(
                            "data[{index}] is {}; every entry must be an object whose keys are \
                             the label's variable field names.",
                            describe(record)
                        ),
                    ));
                }
            }
        }

        let rendered = serde_json::to_value(self).map_err(|error| {
            Error::validation(
                "options",
                format!("could not serialize the options: {error}"),
            )
        })?;

        match rendered {
            // Rule C7: no options means a bare URL, not an empty query string.
            Value::Object(map) if map.is_empty() => Ok(None),
            other => Ok(Some(other.to_string())),
        }
    }
}

fn describe(value: &Value) -> &'static str {
    match value {
        Value::Null => "null",
        Value::Bool(_) => "a boolean",
        Value::Number(_) => "a number",
        Value::String(_) => "a string",
        Value::Array(_) => "an array",
        Value::Object(_) => "an object",
    }
}

/// One conversion.
///
/// Rust gets a value struct rather than the fluent chain the .NET, Node, Java and PHP SDKs
/// expose: a chain that defers its errors to a terminal `send()` is as un-Rust as it is
/// un-Go, and `Option<T>` already expresses "not set" without one.
#[derive(Clone, Debug)]
pub struct ConvertRequest {
    /// The format of [`body`](ConvertRequest::body).
    pub source: SourceFormat,
    /// The format to produce.
    pub target: TargetFormat,
    /// The document. For [`SourceFormat::Url`] it is the URL to fetch, as text.
    pub body: Vec<u8>,
    /// Send the body as base64 `text/plain` rather than the source's own media type. Only
    /// the binary sources support it.
    pub base64_text: bool,
    /// The conversion parameters.
    pub options: ConversionOptions,
}

impl ConvertRequest {
    /// A request with no options set.
    #[must_use]
    pub fn new(source: SourceFormat, target: TargetFormat, body: impl Into<Vec<u8>>) -> Self {
        Self {
            source,
            target,
            body: body.into(),
            base64_text: false,
            options: ConversionOptions::new(),
        }
    }

    /// Replaces the options.
    #[must_use]
    pub fn options(mut self, options: ConversionOptions) -> Self {
        self.options = options;
        self
    }

    /// Sends the body as base64 `text/plain`.
    #[must_use]
    pub fn base64_text(mut self, base64_text: bool) -> Self {
        self.base64_text = base64_text;
        self
    }
}
