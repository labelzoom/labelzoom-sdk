// Translation of conformance/cases/typecheck/source-format-not-accepted-as-target.json:
//     client.convert().fromZpl(body).to(SourceFormat.PDF)
//
// SourceFormat and TargetFormat are distinct enums. Expected: E0308, mismatched types.
use labelzoom::{ConvertRequest, SourceFormat};

fn main() {
    let _ = ConvertRequest::new(SourceFormat::Zpl, SourceFormat::Pdf, "^XA^XZ");
}
