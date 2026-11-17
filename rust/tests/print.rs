//! The print surface through its public types, beyond what the conformance fixtures pin:
//! one request built with the typed setters, and the per-call key generation.

#[allow(dead_code)]
mod support;

use labelzoom::{
    HttpRequest, LabelZoomClient, PrintOptions, PrintRequest, SourceFormat, TemplatePrintRequest,
};
use serde_json::{json, Value};
use std::collections::HashMap;
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
fn a_typed_print_sends_its_source_options_copies_and_key() {
    let transport = RecordingTransport::new(vec![accepted()]);
    let request = PrintRequest::new("printer 7", SourceFormat::Pdf, b"%PDF-1.4".to_vec())
        .options(
            PrintOptions::new()
                .pdf_page(0)
                .label_size(4.0, 6.0)
                .split(2, 2),
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
            "split": { "rows": 2, "columns": 2 }
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
    let request = PrintRequest::new("printer-1", SourceFormat::Zpl, "^XA^XZ");

    client.print(&request).expect("the first print succeeds");
    client.print(&request).expect("the second print succeeds");
    client
        .print_template(&TemplatePrintRequest::new("printer-1", "template-1"))
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
            "printer-1",
            SourceFormat::Jpg,
            b"\xFF\xD8".to_vec(),
        ))
        .expect("the print succeeds");

    let sent = only_request(&transport);
    let (_, query) = split_url(&sent.url);
    assert_eq!(query.get("sourceFormat").map(String::as_str), Some("jpeg"));
    assert!(!query.contains_key("params"));
    assert!(!query.contains_key("copies"));
    assert_eq!(sent.header("content-type"), Some("image/jpeg"));
}
