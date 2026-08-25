//! Anonymous end-to-end check against the live API. Not part of the test suite: the
//! conformance run is offline by design, and this is the one thing fixtures cannot prove.
//!
//! ```sh
//! cargo run --example smoke
//! ```

use labelzoom::{ConversionOptions, ConvertRequest, LabelZoomClient, SourceFormat, TargetFormat};

fn main() -> Result<(), Box<dyn std::error::Error>> {
    let client = LabelZoomClient::builder().anonymous().build()?;
    println!("authenticated: {}", client.is_authenticated());

    let png = client.convert(
        &ConvertRequest::new(
            SourceFormat::Zpl,
            TargetFormat::Png,
            "^XA^FO20,20^A0N,28^FDHello LabelZoom^FS^XZ",
        )
        .options(ConversionOptions::new().label_size(4.0, 6.0)),
    )?;
    println!(
        "status={} contentType={:?} bytes={} requestId={:?}",
        png.status,
        png.content_type,
        png.bytes.len(),
        png.request_id
    );
    println!(
        "png magic: {}",
        png.bytes[..8]
            .iter()
            .map(|b| format!("{b:02x}"))
            .collect::<String>()
    );

    let epl = client.convert(&ConvertRequest::new(
        SourceFormat::Zpl,
        TargetFormat::Epl,
        "^XA^FO20,20^A0N,28^FDHello^FS^XZ",
    ))?;
    println!(
        "epl status={} contentType={:?} text={:?}",
        epl.status,
        epl.content_type,
        epl.text()
    );

    Ok(())
}
