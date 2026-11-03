//! Conversion parameters and their `?params=<JSON>` serialization.

use crate::error::Error;
use crate::formats::{SourceFormat, TargetFormat};
use serde::Serialize;
use serde_json::{Map, Value};

/// How color is reduced when rasterizing. Server default `GRAYSCALE`.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize)]
#[non_exhaustive]
pub enum ColorMode {
    /// Pure black and white.
    #[serde(rename = "BW")]
    Bw,
    /// Grayscale.
    #[serde(rename = "GRAYSCALE")]
    Grayscale,
    /// Full color.
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

/// How pages fill the columns of a multi-column row ([`LayoutOptions::across`] above 1).
/// Server default `SEQUENTIAL`.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize)]
#[non_exhaustive]
pub enum LayoutFill {
    /// Each page goes in the next column.
    #[serde(rename = "SEQUENTIAL")]
    Sequential,
    /// Each page fills the whole row.
    #[serde(rename = "REPEAT")]
    Repeat,
}

/// The pixel offset of the extracted region. Server default 0,0.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Serialize)]
pub struct Position {
    /// Horizontal offset, in pixels.
    pub x: i64,
    /// Vertical offset, in pixels.
    pub y: i64,
}

/// The media size, in INCHES -- not dots, and not millimeters.
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
    /// Image or PDF to ZPL: send each run of identical labels as one label with a `^PQ`
    /// print quantity. Server default `false`.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub collapse_copies: Option<bool>,
}

/// Labels side by side in each printed row, for multi-column roll stock. Image or PDF to
/// ZPL in `IMAGE` mode only.
#[derive(Clone, Copy, Debug, Default, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct LayoutOptions {
    /// Labels across the roll, 1 to 10. Server default 1.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub across: Option<i64>,
    /// Space between two labels in a row, in INCHES. Server default 0.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub gap: Option<f64>,
    /// How pages fill the columns. Server default `SEQUENTIAL`.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub fill: Option<LayoutFill>,
}

/// Cuts each page into a grid of labels, read left to right and then down. Image or PDF to
/// ZPL in `IMAGE` mode only.
///
/// Every length is in INCHES on the page as displayed, measured from its top-left corner.
/// Leave `width` and `height` `None` to have the server divide the page equally.
#[derive(Clone, Copy, Debug, Default, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct SplitOptions {
    /// Rows of labels on each page, 1 to 20. Server default 1.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub rows: Option<i64>,
    /// Columns of labels on each page, 1 to 20. Server default 1.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub columns: Option<i64>,
    /// One label's width on the page. `None` divides the page equally.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub width: Option<f64>,
    /// One label's height on the page. `None` divides the page equally.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub height: Option<f64>,
    /// Distance from the page's left edge to the first column. Server default 0.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub left: Option<f64>,
    /// Distance from the page's top edge to the first row. Server default 0.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub top: Option<f64>,
    /// Space between two columns. Server default 0.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub column_gap: Option<f64>,
    /// Space between two rows. Server default 0.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub row_gap: Option<f64>,
    /// Leave out labels with nothing printable. Server default `false`.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub skip_blank: Option<bool>,
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
    /// Output resolution. Server default 203.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub dpi: Option<i64>,
    /// Degrees clockwise. Must be a multiple of 90. Server default 0.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub rotation: Option<i64>,
    /// Percentage. Server default 100.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub scaling: Option<f64>,
    /// Color reduction. Server default `GRAYSCALE`.
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
    /// Labels side by side in each printed row. Image or PDF to ZPL, IMAGE mode only.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub layout: Option<LayoutOptions>,
    /// Cuts each page into a grid of labels. Image or PDF to ZPL, IMAGE mode only.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub split: Option<SplitOptions>,
    /// ZERO-BASED index of the one label to convert, on every plan. For image or PDF to ZPL
    /// it counts the labels after `split`, page by page. Leave `None` to convert every label.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub label_index: Option<i64>,
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

    /// Sets the output resolution.
    #[must_use]
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

    /// Sets the color mode.
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

    /// Image or PDF to ZPL: sends each run of identical labels as one label with a `^PQ`
    /// print quantity.
    #[must_use]
    pub fn zpl_collapse_copies(mut self, collapse: bool) -> Self {
        self.zpl
            .get_or_insert_with(ZplOptions::default)
            .collapse_copies = Some(collapse);
        self
    }

    /// Image or PDF to ZPL, IMAGE mode: puts `across` labels side by side in each printed row,
    /// `gap_inches` apart. The gap is in **inches**, not dots.
    #[must_use]
    pub fn layout(mut self, across: i64, gap_inches: f64) -> Self {
        let layout = self.layout.get_or_insert_with(LayoutOptions::default);
        layout.across = Some(across);
        layout.gap = Some(gap_inches);
        self
    }

    /// Sets how pages fill the columns of a row.
    #[must_use]
    pub fn layout_fill(mut self, fill: LayoutFill) -> Self {
        self.layout.get_or_insert_with(LayoutOptions::default).fill = Some(fill);
        self
    }

    /// Image or PDF to ZPL, IMAGE mode: cuts each page into `rows` x `columns` labels, read left to
    /// right and then down.
    #[must_use]
    pub fn split(mut self, rows: i64, columns: i64) -> Self {
        let split = self.split.get_or_insert_with(SplitOptions::default);
        split.rows = Some(rows);
        split.columns = Some(columns);
        self
    }

    /// Sets the size of one label on the source page, in **inches**. Leave it unset to
    /// divide the page equally.
    #[must_use]
    pub fn split_label_size(mut self, width_inches: f64, height_inches: f64) -> Self {
        let split = self.split.get_or_insert_with(SplitOptions::default);
        split.width = Some(width_inches);
        split.height = Some(height_inches);
        self
    }

    /// Sets the distance from the page's left and top edges to the first label, in
    /// **inches**.
    #[must_use]
    pub fn split_margins(mut self, left_inches: f64, top_inches: f64) -> Self {
        let split = self.split.get_or_insert_with(SplitOptions::default);
        split.left = Some(left_inches);
        split.top = Some(top_inches);
        self
    }

    /// Sets the space between two columns and between two rows of labels on the page, in
    /// **inches**.
    #[must_use]
    pub fn split_gaps(mut self, column_gap_inches: f64, row_gap_inches: f64) -> Self {
        let split = self.split.get_or_insert_with(SplitOptions::default);
        split.column_gap = Some(column_gap_inches);
        split.row_gap = Some(row_gap_inches);
        self
    }

    /// Leaves out labels with nothing printable, such as the unused end of a partly filled
    /// sheet.
    #[must_use]
    pub fn split_skip_blank(mut self, skip_blank: bool) -> Self {
        self.split
            .get_or_insert_with(SplitOptions::default)
            .skip_blank = Some(skip_blank);
        self
    }

    /// Selects the one label to convert, on every plan. **Zero-based**; for image or PDF to
    /// ZPL it counts the labels after `split`, page by page.
    #[must_use]
    pub fn label_index(mut self, zero_based_index: i64) -> Self {
        self.label_index = Some(zero_based_index);
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
