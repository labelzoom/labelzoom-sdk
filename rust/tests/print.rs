//! The print surface through its public types, beyond what the conformance fixtures pin:
//! one request built with the typed setters, the printable sources, the per-call key
//! generation, and the shape test that keeps print's options in step with convert's.

#[allow(dead_code)]
mod support;

use labelzoom::{
    Error, HttpRequest, LabelZoomClient, PrintOptions, PrintRequest, SourceFormat,
    TemplatePrintRequest,
};
use serde_json::{json, Value};
use std::collections::{BTreeSet, HashMap};
use std::sync::Arc;
use support::RecordingTransport;

fn accepted() -> Value {
    json!({
        "status": 200,
        "headers": { "content-type": "application/json" },
        "bodyText": r#"{"jobId":"job-1","status":"dispatched","copies":3}"#
    })
}

fn client(transport: &Arc<RecordingTransport>) -> LabelZoomClient {
    LabelZoomClient::builder()
        .api_key("lz_test_print_key")
        .transport(Arc::clone(transport) as Arc<dyn labelzoom::Transport>)
        .env_lookup(|_| None)
        .max_retries(0)
        .build()
        .expect("the client builds")
}

/// Splits a URL this crate wrote into its path and decoded query parameters.
fn split_url(url: &str) -> (String, HashMap<String, String>) {
    let (before, query) = url.split_once('?').unwrap_or((url, ""));
    let path = before
        .split_once("://")
        .and_then(|(_, rest)| rest.find('/').map(|index| rest[index..].to_owned()))
        .unwrap_or_default();
    let query = query
        .split('&')
        .filter(|pair| !pair.is_empty())
        .map(|pair| {
            let (name, value) = pair.split_once('=').unwrap_or((pair, ""));
            (name.to_owned(), percent_decode(value))
        })
        .collect();
    (path, query)
}

fn percent_decode(value: &str) -> String {
    let bytes = value.as_bytes();
    let mut out = Vec::new();
    let mut index = 0;
    while index < bytes.len() {
        if bytes[index] == b'%' {
            let hex = std::str::from_utf8(&bytes[index + 1..index + 3]).unwrap();
            out.push(u8::from_str_radix(hex, 16).unwrap());
            index += 3;
        } else {
            out.push(bytes[index]);
            index += 1;
        }
    }
    String::from_utf8(out).unwrap()
}

fn only_request(transport: &RecordingTransport) -> HttpRequest {
    let requests = transport.requests.lock().unwrap();
    assert_eq!(requests.len(), 1, "exactly one request is sent");
    requests[0].clone()
}

#[test]
fn a_typed_print_sends_its_source_options_rotation_copies_and_key() {
    let transport = RecordingTransport::new(vec![accepted()]);
    let request = PrintRequest::new(SourceFormat::Pdf, b"%PDF-1.4".to_vec(), "printer 7")
        .options(
            PrintOptions::new()
                .pdf_page(0)
                .label_size(4.0, 6.0)
                .split(2, 2)
                .rotation(90),
        )
        .copies(3)
        .idempotency_key("SO-1001");

    let job = client(&transport)
        .print(&request)
        .expect("the print succeeds");
    assert_eq!(job.job_id, "job-1");
    assert_eq!(job.copies, Some(3));

    let sent = only_request(&transport);
    let (path, query) = split_url(&sent.url);
    assert_eq!(sent.method, "POST");
    // Each id is percent-encoded as a path segment (rule P1).
    assert_eq!(path, "/api/v3/printers/printer%207/print");
    assert_eq!(query.get("sourceFormat").map(String::as_str), Some("pdf"));
    assert_eq!(query.get("copies").map(String::as_str), Some("3"));
    let params: Value = serde_json::from_str(&query["params"]).expect("params is JSON");
    assert_eq!(
        params,
        json!({
            "pdf": { "pageNumber": 0 },
            "label": { "width": 4.0, "height": 6.0 },
            "split": { "rows": 2, "columns": 2 },
            "rotation": 90
        })
    );
    assert_eq!(sent.header("content-type"), Some("application/pdf"));
    assert_eq!(sent.header("accept"), Some("application/json"));
    assert_eq!(sent.header("idempotency-key"), Some("SO-1001"));
    assert_eq!(
        sent.header("authorization"),
        Some("Bearer lz_test_print_key")
    );
    assert_eq!(sent.body, b"%PDF-1.4");
}

#[test]
fn two_calls_generate_two_different_keys() {
    let transport = RecordingTransport::new(vec![accepted(), accepted(), accepted()]);
    let client = client(&transport);
    let request = PrintRequest::new(SourceFormat::Zpl, "^XA^XZ", "printer-1");

    client.print(&request).expect("the first print succeeds");
    client.print(&request).expect("the second print succeeds");
    client
        .print_template(&TemplatePrintRequest::new("template-1", "printer-1"))
        .expect("the template print succeeds");

    let keys: Vec<String> = transport
        .requests
        .lock()
        .unwrap()
        .iter()
        .map(|request| {
            request
                .header("idempotency-key")
                .expect("every print carries a key")
                .to_owned()
        })
        .collect();
    assert_eq!(keys.len(), 3);
    assert_ne!(
        keys[0], keys[1],
        "the same request sent twice is two prints"
    );
    assert_ne!(keys[1], keys[2]);
    assert_ne!(keys[0], keys[2]);
}

#[test]
fn a_print_with_no_options_sends_no_params() {
    let transport = RecordingTransport::new(vec![accepted()]);
    client(&transport)
        .print(&PrintRequest::new(
            SourceFormat::Jpg,
            b"\xFF\xD8".to_vec(),
            "printer-1",
        ))
        .expect("the print succeeds");

    let sent = only_request(&transport);
    let (_, query) = split_url(&sent.url);
    assert_eq!(query.get("sourceFormat").map(String::as_str), Some("jpeg"));
    assert!(!query.contains_key("params"));
    assert!(!query.contains_key("copies"));
    assert_eq!(sent.header("content-type"), Some("image/jpeg"));
}

#[test]
fn every_source_prints_except_a_url() {
    for &source in SourceFormat::ALL {
        assert_eq!(
            source.is_printable(),
            source != SourceFormat::Url,
            "{source} printable"
        );
    }
}

#[test]
fn a_url_is_refused_before_anything_is_sent() {
    let transport = RecordingTransport::new(vec![]);
    let result = client(&transport).print(&PrintRequest::new(
        SourceFormat::Url,
        "https://example.com/label.zpl",
        "printer-1",
    ));

    match result {
        Err(Error::Validation(error)) => assert_eq!(error.parameter, "source"),
        other => panic!("expected a validation error, got {other:?}"),
    }
    assert!(transport.requests.lock().unwrap().is_empty());
}

#[test]
fn a_design_file_prints_as_its_own_bytes() {
    let transport = RecordingTransport::new(vec![accepted()]);
    client(&transport)
        .print(&PrintRequest::new(
            SourceFormat::Btw,
            vec![0x00, 0xFF, 0x10],
            "printer-1",
        ))
        .expect("the print succeeds");

    let sent = only_request(&transport);
    let (_, query) = split_url(&sent.url);
    assert_eq!(query.get("sourceFormat").map(String::as_str), Some("btw"));
    assert!(!query.contains_key("params"));
    assert_eq!(
        sent.header("content-type"),
        Some("application/octet-stream")
    );
    assert_eq!(sent.body, [0x00, 0xFF, 0x10]);
}

#[test]
fn a_bad_rotation_is_refused_like_a_conversion_one() {
    let transport = RecordingTransport::new(vec![]);
    let result = client(&transport).print(
        &PrintRequest::new(SourceFormat::Zpl, "^XA^XZ", "printer-1")
            .options(PrintOptions::new().rotation(45)),
    );

    match result {
        Err(Error::Validation(error)) => assert_eq!(error.parameter, "rotation"),
        other => panic!("expected a validation error, got {other:?}"),
    }
    assert!(transport.requests.lock().unwrap().is_empty());
}

// --------------------------------------------------------------------------- shape
//
// The Rust form of the stage-shape test (contract section 1.2). Rust has no reflection, so
// it reads the public setters out of the source. Every `ConversionOptions` setter is sorted
// into a source or a target option below: a setter added to convert and not sorted fails
// here, as does a source option missing from print or a target option other than rotation
// leaking into it.

/// How the document is read. Print takes every one of these.
const SOURCE_OPTIONS: &[&str] = &[
    "color_mode",
    "darkness",
    "extra",
    "label_index",
    "label_size",
    "pdf_conversion_mode",
    "pdf_page",
    "position",
    "scaling",
    "source_dpi",
    "split",
    "split_gaps",
    "split_label_size",
    "split_margins",
    "split_skip_blank",
    "with_data",
    "zpl_commands_to_ignore",
];

/// What changes the output. Print takes only `rotation` of these.
const TARGET_OPTIONS: &[&str] = &[
    "dialect",
    "layout",
    "layout_fill",
    "rotation",
    "target_dpi",
    "watermark",
    "zpl_collapse_copies",
    "zpl_image_compression",
];

/// The `pub fn` names declared directly in `impl <type> {` in one source file.
fn public_methods(file: &str, type_name: &str) -> BTreeSet<String> {
    let path = format!("{}/src/{file}", env!("CARGO_MANIFEST_DIR"));
    // Normalized, so a checkout with CRLF line endings reads the same.
    let source = std::fs::read_to_string(&path)
        .expect("the source file reads")
        .replace("\r\n", "\n");
    let header = format!("\nimpl {type_name} {{\n");
    let start = source
        .find(&header)
        .unwrap_or_else(|| panic!("no `impl {type_name}` block in {file}"));
    let block = &source[start + header.len()..];
    let block = &block[..block.find("\n}\n").expect("the impl block closes")];
    block
        .lines()
        .filter_map(|line| line.strip_prefix("    pub fn "))
        .map(|rest| rest.split(['(', '<']).next().unwrap_or_default().to_owned())
        .collect()
}

fn names(lists: &[&[&str]]) -> BTreeSet<String> {
    lists
        .iter()
        .flat_map(|list| list.iter().map(|name| (*name).to_owned()))
        .collect()
}

#[test]
fn every_conversion_option_is_sorted_into_source_or_target() {
    let source = names(&[SOURCE_OPTIONS]);
    let target = names(&[TARGET_OPTIONS]);
    assert!(source.is_disjoint(&target));
    assert_eq!(
        public_methods("options.rs", "ConversionOptions"),
        names(&[SOURCE_OPTIONS, TARGET_OPTIONS, &["new"]])
    );
}

#[test]
fn print_options_are_the_source_options_plus_rotation() {
    assert_eq!(
        public_methods("print.rs", "PrintOptions"),
        names(&[
            SOURCE_OPTIONS,
            &["rotation", "new", "as_conversion_options"]
        ])
    );
}

#[test]
fn the_printer_target_offers_options_copies_and_a_key_only() {
    // No base64-text setter: print reads the body as the format's own bytes.
    assert_eq!(
        public_methods("print.rs", "PrintRequest"),
        names(&[&["new", "options", "copies", "idempotency_key"]])
    );
    assert_eq!(
        public_methods("print.rs", "TemplatePrintRequest"),
        names(&[&["new", "with_data", "validate", "copies", "idempotency_key"]])
    );
}
