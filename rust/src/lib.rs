//! Official Rust client for the [LabelZoom API](https://api.labelzoom.com).
//!
//! LabelZoom converts barcode labels between printer languages (`ZPL`, `EPL`, `IPL`,
//! `TSPL`, `DPL`, `SBPL`), LabelZoom's own XML/JSON model, PDF, and raster images. Almost everything the API does
//! happens at one endpoint:
//!
//! ```text
//! POST https://api.labelzoom.com/api/v2/convert/{sourceFormat}/to/{targetFormat}
//! ```
//!
//! Authentication is optional. Without a credential the API serves a free tier:
//! watermarked output, the first label only, a 1 MB request cap, and no multi-page,
//! JSON-target or image-to-image conversion.
//!
//! ```no_run
//! use labelzoom::{ConversionOptions, ConvertRequest, LabelZoomClient, SourceFormat, TargetFormat};
//!
//! # fn main() -> Result<(), Box<dyn std::error::Error>> {
//! let client = LabelZoomClient::new();          // anonymous; this works
//!
//! let request = ConvertRequest::new(
//!     SourceFormat::Zpl,
//!     TargetFormat::Png,
//!     "^XA^FO20,20^A0N,28^FDHello^FS^XZ",
//! )
//! .options(ConversionOptions::new().dpi(300).label_size(4.0, 6.0));
//!
//! client.convert(&request)?.save("label.png")?;
//! # Ok(())
//! # }
//! ```
//!
//! The behaviour of every LabelZoom SDK is specified in `docs/API_CONTRACT.md` and checked
//! by the shared fixtures in `conformance/`, which this crate's test suite executes.

#![forbid(unsafe_code)]
#![warn(missing_docs, clippy::pedantic)]
#![allow(
    clippy::must_use_candidate,
    clippy::missing_errors_doc,
    clippy::missing_panics_doc,
    clippy::module_name_repetitions,
    // ClientBuilder::api_key is Option<Option<String>> deliberately: the outer layer says
    // whether the caller configured a credential at all, the inner whether it was a value
    // or an explicit "none". Rule G2 turns on exactly that difference.
    clippy::option_option
)]

mod client;
mod error;
mod formats;
mod options;
mod result;
mod transport;

pub use client::{ClientBuilder, LabelZoomClient, API_KEY_ENV_VAR, DEFAULT_BASE_URL, VERSION};
pub use error::{ApiError, ApiErrorKind, Error, TransportError, ValidationError};
pub use formats::{SourceFormat, TargetFormat};
pub use options::{
    ColorMode, ConversionOptions, ConvertRequest, LabelSize, PdfConversionMode, PdfOptions,
    Position, ZplImageCompression, ZplOptions,
};
pub use result::ConversionResult;
pub use transport::{HttpRequest, HttpResponse, Sleeper, ThreadSleeper, Transport};

#[cfg(feature = "ureq-transport")]
pub use transport::UreqTransport;
