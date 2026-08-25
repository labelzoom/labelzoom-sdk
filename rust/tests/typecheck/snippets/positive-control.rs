// Not a conformance case: the anti-tautology guard for the typecheck harness.
//
// A harness bug that reported "did not compile" unconditionally -- a bad manifest, a
// missing path dependency, a stale target dir -- would make both real typecheck cases
// pass forever. This snippet is the same shape and MUST compile.
use labelzoom::{ConvertRequest, SourceFormat, TargetFormat};

fn main() {
    let _ = ConvertRequest::new(SourceFormat::Zpl, TargetFormat::Pdf, "^XA^XZ");
}
