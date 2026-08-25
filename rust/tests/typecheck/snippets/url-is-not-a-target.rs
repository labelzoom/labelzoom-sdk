// Translation of conformance/cases/typecheck/url-is-not-a-target.json:
//     client.convert().fromZpl(body).to(URL)
//
// URL is source-only -- it is a fetch instruction, not a format -- so there is no
// TargetFormat variant to name. Expected: E0599, no variant named `Url`.
use labelzoom::{ConvertRequest, SourceFormat, TargetFormat};

fn main() {
    let _ = ConvertRequest::new(SourceFormat::Zpl, TargetFormat::Url, "^XA^XZ");
}
