//! Runs the shared conformance fixtures against the Rust SDK.
//!
//! Entirely offline: the client talks to a `RecordingTransport` implementing the crate's
//! own `Transport` trait, so no socket is ever opened and this passes identically on a
//! fork pull request with no secrets. The fixtures are the same ones the .NET, Node, Java,
//! Python, PHP, Go and Ruby suites run -- see `docs/CONFORMANCE.md`.
//!
//! `harness = false` and `libtest-mimic`: one reported trial per fixture id, so a JUnit
//! report from this suite names the same cases every other language's does.

mod support;
mod typecheck;

use labelzoom::{
    ApiErrorKind, ColorMode, ConversionOptions, ConvertRequest, Error, HttpRequest,
    LabelZoomClient, LayoutFill, PdfConversionMode, PrintOptions, PrintRequest, SourceFormat,
    TargetFormat, TemplatePrintRequest, ZplImageCompression,
};
use libtest_mimic::{Arguments, Failed, Trial};
use serde_json::{Map, Value};
use std::collections::BTreeSet;
use std::path::Path;
use std::sync::{Arc, Mutex};
use support::{
    base64_encode, conformance_root, read_json, RecordingSleeper, RecordingTransport, LANGUAGE,
};

/// Case ids that actually ran, so the completeness trial can assert coverage rather than
/// assume it.
static EXECUTED: Mutex<BTreeSet<String>> = Mutex::new(BTreeSet::new());

fn main() -> std::process::ExitCode {
    let arguments = Arguments::from_args();
    let root = conformance_root();
    let spec = read_json(&root.join("spec.json"));

    let all_case_ids: Vec<String> = spec["cases"]
        .as_array()
        .expect("spec.json has no cases array")
        .iter()
        .map(|id| id.as_str().expect("a case id must be a string").to_owned())
        .collect();

    let skips = load_skips(&root);
    let expected: Vec<String> = all_case_ids
        .iter()
        .filter(|id| !skips.iter().any(|(skipped, _)| skipped == *id))
        .cloned()
        .collect();

    let mut trials: Vec<Trial> = expected
        .iter()
        .map(|case_id| {
            let case_id = case_id.clone();
            let path = root.join("cases").join(format!("{case_id}.json"));
            Trial::test(case_id.clone(), move || run_case(&case_id, &path))
        })
        .collect();

    // Not a conformance case: the guard that keeps the two typecheck trials honest. A
    // harness that reported "did not compile" unconditionally would pass them forever.
    trials.push(Trial::test(
        "typecheck-harness/positive-control-compiles",
        typecheck::positive_control,
    ));

    // Fixture-set integrity: order-independent, so it is a normal trial and shows up in
    // the report by name.
    let all_for_trial = all_case_ids.clone();
    let skips_for_trial = skips.clone();
    let generated: BTreeSet<String> = trials.iter().map(|trial| trial.name().to_owned()).collect();
    let expected_set: BTreeSet<String> = expected.iter().cloned().collect();
    trials.push(Trial::test("conformance/declares-every-case", move || {
        let case_count = usize::try_from(spec["caseCount"].as_u64().unwrap_or_default())?;
        if case_count != all_for_trial.len() {
            return Err(format!(
                "spec.json caseCount is {case_count} but it lists {} cases",
                all_for_trial.len()
            )
            .into());
        }
        for (case_id, reason) in &skips_for_trial {
            if !all_for_trial.contains(case_id) {
                return Err(
                    format!("skips/{LANGUAGE}.json declares unknown case '{case_id}'").into(),
                );
            }
            if reason.trim().is_empty() {
                return Err(format!("skip '{case_id}' has no reason").into());
            }
        }
        // Every expected case has a trial. That a trial RAN is checked after the run --
        // see below.
        let missing: Vec<&String> = expected_set.difference(&generated).collect();
        if !missing.is_empty() {
            return Err(format!("no trial was generated for: {missing:?}").into());
        }
        Ok(())
    }));

    let conclusion = libtest_mimic::run(&arguments, trials);

    // The whole anti-drift mechanism. A suite that quietly runs a subset of the fixtures
    // reports success exactly like one that runs all of them, so coverage is asserted.
    //
    // Checked HERE rather than in a trial because libtest-mimic runs trials in parallel:
    // a trial reading this set could be scheduled before the cases it audits, which is
    // exactly what happened the first time it was written that way. Skipped when the
    // caller passed a filter, so `cargo test -- request/auth` still works locally.
    // Only meaningful when this process actually ran the trials in-process, which is
    // `cargo test`. Two other modes reach here:
    //
    //   --list          libtest_mimic::run prints the trial names and runs nothing. This is
    //                   also how cargo-nextest enumerates, so failing here fails the whole
    //                   nextest invocation before a single test runs.
    //   cargo-nextest   one PROCESS per trial, so every process sees an EXECUTED set
    //                   holding at most its own case.
    //
    // Under nextest the guarantee is not lost, it moves: `conformance/declares-every-case`
    // asserts a trial exists for every expected case, nextest runs every trial it is given,
    // and any failure fails the run. That composes to the same thing. It is checked here as
    // well because `cargo test` is what a contributor runs locally, and an accumulator is a
    // stronger statement than a trial list when it is available.
    let under_nextest = std::env::var_os("NEXTEST").is_some();
    let filtered = arguments.filter.is_some() || !arguments.skip.is_empty();
    if !arguments.list && !under_nextest && !filtered {
        let executed = EXECUTED.lock().unwrap().clone();
        let want: BTreeSet<String> = expected.iter().cloned().collect();
        if executed != want {
            let missing: Vec<&String> = want.difference(&executed).collect();
            eprintln!(
                "\nCONFORMANCE COVERAGE FAILURE: executed {} of {} expected cases.\nMissing: {missing:?}",
                executed.len(),
                want.len()
            );
            return std::process::ExitCode::FAILURE;
        }
        eprintln!(
            "conformance: executed all {} expected cases ({} declared, {} skipped)",
            want.len(),
            all_case_ids.len(),
            skips.len()
        );
    }

    conclusion.exit_code()
}

fn load_skips(root: &std::path::Path) -> Vec<(String, String)> {
    let path = root.join("skips").join(format!("{LANGUAGE}.json"));
    if !path.exists() {
        return Vec::new();
    }
    let file = read_json(&path);
    assert_eq!(
        file["language"].as_str(),
        Some(LANGUAGE),
        "skips/{LANGUAGE}.json declares the wrong language"
    );
    file["skips"]
        .as_array()
        .map(|skips| {
            skips
                .iter()
                .map(|skip| {
                    (
                        skip["id"].as_str().unwrap_or_default().to_owned(),
                        skip["reason"].as_str().unwrap_or_default().to_owned(),
                    )
                })
                .collect()
        })
        .unwrap_or_default()
}

fn run_case(case_id: &str, path: &Path) -> Result<(), Failed> {
    let fixture = read_json(path);
    let given = &fixture["given"];
    let expect = &fixture["expect"];

    match case_id.split('/').next() {
        Some("request") => run_request_case(given, expect)?,
        Some("response") => run_response_case(given, expect)?,
        Some("retry") => run_retry_case(given, expect)?,
        Some("validation") => run_validation_case(given, expect)?,
        Some("typecheck") => typecheck::run(case_id, given)?,
        Some("print") => run_print_case(given, expect)?,
        _ => return Err(format!("unknown case kind for '{case_id}'").into()),
    }

    EXECUTED.lock().unwrap().insert(case_id.to_owned());
    Ok(())
}

// ------------------------------------------------------------------- translation

struct Outcome {
    result: Option<labelzoom::ConversionResult>,
    error: Option<Error>,
    transport: Arc<RecordingTransport>,
    sleeper: Arc<RecordingSleeper>,
    env_lookups: Arc<Mutex<Vec<String>>>,
}

/// Translates the fixture's wire-shaped options onto `ConversionOptions`.
///
/// This translation layer is the only per-language code in a runner, and writing it is
/// what proves the Rust divergence in `API_CONTRACT.md` section 9 is skin deep.
fn build_options(given: &Value) -> Result<ConversionOptions, Failed> {
    let mut options = ConversionOptions::new();
    let Some(wire) = given.get("options").and_then(Value::as_object) else {
        return Ok(options);
    };

    for (key, value) in wire {
        match key.as_str() {
            "sourceDpi" => options.source_dpi = value.as_i64(),
            "targetDpi" => options.target_dpi = value.as_i64(),
            "rotation" => options.rotation = value.as_i64(),
            "scaling" => options.scaling = value.as_f64(),
            "colorMode" => {
                options.color_mode = Some(match value.as_str().unwrap_or_default() {
                    "BW" => ColorMode::Bw,
                    "GRAYSCALE" => ColorMode::Grayscale,
                    "COLOR" => ColorMode::Color,
                    other => return Err(format!("unknown colorMode '{other}'").into()),
                });
            }
            "darkness" => options.darkness = value.as_i64(),
            "position" => {
                options = options.position(
                    value["x"].as_i64().unwrap_or_default(),
                    value["y"].as_i64().unwrap_or_default(),
                );
            }
            "watermark" => options.watermark = value.as_bool(),
            "dialect" => options.dialect = value.as_str().map(str::to_owned),
            "data" => {
                // Deliberately NOT coerced to objects here. Typing this as Vec<Value>
                // is what lets Rust run validation/data-element-not-an-object rather
                // than declaring it away the way Java has to.
                options.data = Some(match value {
                    Value::Array(records) => records.clone(),
                    single => vec![single.clone()],
                });
            }
            "label" => {
                options.label = Some(labelzoom::LabelSize {
                    width: value.get("width").and_then(Value::as_f64),
                    height: value.get("height").and_then(Value::as_f64),
                });
            }
            "pdf" => {
                let mut pdf = labelzoom::PdfOptions::default();
                if let Some(mode) = value.get("conversionMode").and_then(Value::as_str) {
                    pdf.conversion_mode = Some(match mode {
                        "IMAGE" => PdfConversionMode::Image,
                        "NATIVE" => PdfConversionMode::Native,
                        other => return Err(format!("unknown conversionMode '{other}'").into()),
                    });
                }
                pdf.page_number = value.get("pageNumber").and_then(Value::as_i64);
                options.pdf = Some(pdf);
            }
            "zpl" => {
                let mut zpl = labelzoom::ZplOptions::default();
                if let Some(commands) = value.get("commandsToIgnore").and_then(Value::as_array) {
                    zpl.commands_to_ignore = Some(
                        commands
                            .iter()
                            .map(|command| command.as_str().unwrap_or_default().to_owned())
                            .collect(),
                    );
                }
                if let Some(compression) = value.get("imageCompression").and_then(Value::as_str) {
                    zpl.image_compression = Some(match compression {
                        "Z64" => ZplImageCompression::Z64,
                        "COMPRESSED_HEX" => ZplImageCompression::CompressedHex,
                        other => return Err(format!("unknown imageCompression '{other}'").into()),
                    });
                }
                zpl.collapse_copies = value.get("collapseCopies").and_then(Value::as_bool);
                options.zpl = Some(zpl);
            }
            "layout" => {
                let mut layout = labelzoom::LayoutOptions {
                    across: value.get("across").and_then(Value::as_i64),
                    gap: value.get("gap").and_then(Value::as_f64),
                    ..labelzoom::LayoutOptions::default()
                };
                if let Some(fill) = value.get("fill").and_then(Value::as_str) {
                    layout.fill = Some(match fill {
                        "SEQUENTIAL" => LayoutFill::Sequential,
                        "REPEAT" => LayoutFill::Repeat,
                        other => return Err(format!("unknown layout fill '{other}'").into()),
                    });
                }
                options.layout = Some(layout);
            }
            "split" => {
                let inches = |name: &str| value.get(name).and_then(Value::as_f64);
                options.split = Some(labelzoom::SplitOptions {
                    rows: value.get("rows").and_then(Value::as_i64),
                    columns: value.get("columns").and_then(Value::as_i64),
                    width: inches("width"),
                    height: inches("height"),
                    left: inches("left"),
                    top: inches("top"),
                    column_gap: inches("columnGap"),
                    row_gap: inches("rowGap"),
                    skip_blank: value.get("skipBlank").and_then(Value::as_bool),
                });
            }
            "labelIndex" => options.label_index = value.as_i64(),
            other => {
                return Err(format!(
                    "Fixture sets option '{other}', which the Rust runner does not map. \
                     Add it to build_options rather than skipping the case."
                )
                .into())
            }
        }
    }

    Ok(options)
}

/// The stubs a client was built on, kept for the assertions.
struct Harness {
    client: LabelZoomClient,
    transport: Arc<RecordingTransport>,
    sleeper: Arc<RecordingSleeper>,
    env_lookups: Arc<Mutex<Vec<String>>>,
}

/// Builds a client on recording stubs, configured from the fixture's `client`, `env` and
/// `maxRetries`. Shared by every case kind, so a print case resolves credentials, base URL
/// and retries exactly as a convert case does.
fn build_client(
    given: &Value,
    script: Vec<Value>,
    default_max_retries: Option<u32>,
) -> Result<Harness, Failed> {
    let transport = RecordingTransport::new(script);
    let sleeper = Arc::new(RecordingSleeper::default());
    let env_lookups = Arc::new(Mutex::new(Vec::new()));

    let env_values: Map<String, Value> = given
        .get("env")
        .and_then(Value::as_object)
        .cloned()
        .unwrap_or_default();
    let recorded = Arc::clone(&env_lookups);

    let mut builder = LabelZoomClient::builder()
        .transport(Arc::clone(&transport) as Arc<dyn labelzoom::Transport>)
        .sleeper(Arc::clone(&sleeper) as Arc<dyn labelzoom::Sleeper>)
        // The fixtures assert exact sleep durations, so the backoff has to be deterministic.
        .jitter(false)
        .env_lookup(move |key| {
            recorded.lock().unwrap().push(key.to_owned());
            env_values
                .get(key)
                .and_then(Value::as_str)
                .map(str::to_owned)
        });

    match given.get("client") {
        // A case with no client block is not exercising credential resolution. Force
        // anonymous so a stray LABELZOOM_API_KEY cannot change the outcome.
        None => builder = builder.anonymous(),
        Some(client) => {
            // Present-and-null means "no credential"; omitting the key entirely means
            // "read the environment", which is the case that must leave the builder alone.
            if let Some(api_key) = client.get("apiKey") {
                builder = match api_key.as_str() {
                    Some(key) => builder.api_key(key),
                    None => builder.anonymous(),
                };
            }
            if let Some(base_url) = client.get("baseUrl").and_then(Value::as_str) {
                builder = builder.base_url(base_url);
            }
        }
    }

    let max_retries = given
        .get("maxRetries")
        .and_then(Value::as_u64)
        .map(|value| u32::try_from(value).unwrap_or(u32::MAX))
        .or(default_max_retries);
    if let Some(max_retries) = max_retries {
        builder = builder.max_retries(max_retries);
    }

    let client = builder
        .build()
        .map_err(|error| format!("could not build the client: {error}"))?;

    Ok(Harness {
        client,
        transport,
        sleeper,
        env_lookups,
    })
}

/// Executes one convert fixture and returns everything the assertions need.
fn execute(
    given: &Value,
    script: Vec<Value>,
    default_max_retries: Option<u32>,
) -> Result<Outcome, Failed> {
    let Harness {
        client,
        transport,
        sleeper,
        env_lookups,
    } = build_client(given, script, default_max_retries)?;

    let request = ConvertRequest::new(
        SourceFormat::from_name(given["source"].as_str().unwrap_or_default())
            .ok_or_else(|| format!("unknown source '{}'", given["source"]))?,
        // from_name returns None for a source-only format, which is the runtime half of
        // what the typecheck cases assert at compile time.
        TargetFormat::from_name(given["target"].as_str().unwrap_or_default())
            .ok_or_else(|| format!("unknown target '{}'", given["target"]))?,
        given["bodyText"].as_str().unwrap_or_default(),
    )
    .base64_text(given.get("sourceEncoding").and_then(Value::as_str) == Some("base64text"))
    .options(build_options(given)?);

    let (result, error) = match client.convert(&request) {
        Ok(result) => (Some(result), None),
        Err(error) => (None, Some(error)),
    };

    Ok(Outcome {
        result,
        error,
        transport,
        sleeper,
        env_lookups,
    })
}

// ------------------------------------------------------------------ case runners

fn run_request_case(given: &Value, expect: &Value) -> Result<(), Failed> {
    let ok_response = serde_json::json!({
        "status": 200,
        "headers": { "content-type": "text/plain" },
        "bodyText": "^XA^XZ"
    });
    let outcome = execute(given, vec![ok_response], None)?;
    if let Some(error) = outcome.error {
        return Err(format!("unexpected error: {error}").into());
    }

    let request = outcome.last_request()?;
    assert_request(&request, expect)?;

    // Rule G2's negative half: an explicitly empty API key must force anonymous WITHOUT
    // consulting the environment. An injected lookup is the only way to assert that.
    if given.pointer("/client/apiKey").and_then(Value::as_str) == Some("") {
        let lookups = outcome.env_lookups.lock().unwrap();
        if !lookups.is_empty() {
            return Err(format!(
                "an empty API key must force anonymous without an environment lookup, \
                 but the SDK looked up {lookups:?}"
            )
            .into());
        }
    }

    Ok(())
}

/// Asserts one recorded request against the `request` vocabulary (`CONFORMANCE.md`
/// section 2), plus the two members print cases add: `query` and `bodyJson`.
fn assert_request(request: &HttpRequest, expect: &Value) -> Result<(), Failed> {
    let url = url::Parts::parse(&request.url)?;

    if let Some(method) = expect.get("method").and_then(Value::as_str) {
        check(request.method == method, "method", request.method, method)?;
    }
    if let Some(expected) = expect.get("url").and_then(Value::as_str) {
        check(
            url.origin_and_path == expected,
            "url",
            &url.origin_and_path,
            expected,
        )?;
    }
    if let Some(expected) = expect.get("path").and_then(Value::as_str) {
        check(url.path == expected, "path", &url.path, expected)?;
    }

    // HttpRequest::header compares case-insensitively, which is the semantics the fixtures
    // assume. expect.headers is a subset assertion; headersAbsent is exact.
    for (name, value) in object(expect, "headers") {
        let actual = request.header(&name).unwrap_or_default();
        let expected = value.as_str().unwrap_or_default();
        check(
            actual == expected,
            &format!("header {name}"),
            actual,
            expected,
        )?;
    }
    for name in array(expect, "headersAbsent") {
        let name = name.as_str().unwrap_or_default();
        if request.header(name).is_some() {
            return Err(format!("header {name} must be absent").into());
        }
    }
    for (name, pattern) in object(expect, "headersMatch") {
        let actual = request.header(&name).unwrap_or_default();
        let pattern = pattern.as_str().unwrap_or_default();
        if !matches_pattern(actual, pattern)? {
            return Err(format!("header {name} = {actual:?}, want match {pattern:?}").into());
        }
    }
    for (name, pattern) in object(expect, "headersNotMatch") {
        let actual = request.header(&name).unwrap_or_default();
        let pattern = pattern.as_str().unwrap_or_default();
        if matches_pattern(actual, pattern)? {
            return Err(
                format!("header {name} = {actual:?}, want NO match for {pattern:?}").into(),
            );
        }
    }

    // Plain query parameters, compared exactly after decoding.
    for (name, expected) in object(expect, "query") {
        let actual = url.query.get(&name).map(String::as_str);
        let expected = expected.as_str();
        check(
            actual == expected,
            &format!("query {name}"),
            actual,
            expected,
        )?;
    }

    // Structural, not textual: JSON key order differs per language and percent-encoding
    // differs per standard library, so comparing encoded strings would be flake by
    // construction.
    for (name, expected) in object(expect, "queryJson") {
        let raw = url
            .query
            .get(&name)
            .ok_or_else(|| format!("query parameter {name} is missing"))?;
        let actual: Value = serde_json::from_str(raw)
            .map_err(|error| format!("query parameter {name} is not JSON: {error}"))?;
        if !json_equivalent(&actual, &expected) {
            return Err(format!("query {name} = {actual}, want {expected}").into());
        }
    }
    for name in array(expect, "queryAbsent") {
        let name = name.as_str().unwrap_or_default();
        if url.query.contains_key(name) {
            return Err(format!("query parameter {name} must be absent").into());
        }
    }
    for (name, keys) in object(expect, "queryJsonAbsentKeys") {
        let raw = url
            .query
            .get(&name)
            .ok_or_else(|| format!("query parameter {name} is missing"))?;
        let actual: Value = serde_json::from_str(raw)?;
        for key in keys.as_array().into_iter().flatten() {
            let key = key.as_str().unwrap_or_default();
            if actual.get(key).is_some() {
                return Err(format!("{name}.{key} must not be serialized").into());
            }
        }
    }

    if let Some(expected) = expect.get("bodyText").and_then(Value::as_str) {
        let actual = String::from_utf8_lossy(&request.body);
        check(actual == expected, "body", &actual, expected)?;
    }
    if let Some(expected) = expect.get("bodyJson") {
        let actual: Value = serde_json::from_slice(&request.body)
            .map_err(|error| format!("the request body is not JSON: {error}"))?;
        if !json_equivalent(&actual, expected) {
            return Err(format!("body = {actual}, want {expected}").into());
        }
    }

    Ok(())
}

fn run_response_case(given: &Value, expect: &Value) -> Result<(), Failed> {
    // Response cases queue one response and assert how it maps. Retry is the subject of
    // retry/*, and leaving it on would consume responses that do not exist for the 429 and
    // 5xx cases.
    let call = serde_json::json!({ "source": "zpl", "target": "zpl", "bodyText": "^XA^XZ" });
    let outcome = execute(&call, vec![given.clone()], Some(0))?;

    if let Some(expected) = expect.get("error") {
        return assert_error(expected, outcome.error.as_ref());
    }

    if let Some(error) = outcome.error {
        return Err(format!("unexpected error: {error}").into());
    }
    let result = outcome.result.expect("a successful case has a result");
    let expected = &expect["result"];

    if let Some(status) = expected.get("status").and_then(Value::as_u64) {
        check(
            u64::from(result.status) == status,
            "status",
            result.status,
            status,
        )?;
    }
    if let Some(content_type) = expected.get("contentType").and_then(Value::as_str) {
        let actual = result.content_type.as_deref().unwrap_or_default();
        check(actual == content_type, "contentType", actual, content_type)?;
    }
    if let Some(text) = expected.get("text").and_then(Value::as_str) {
        let actual = result.text();
        check(actual == text, "text", &actual, text)?;
    }
    if let Some(bytes) = expected.get("bytesBase64").and_then(Value::as_str) {
        let actual = base64_encode(&result.bytes);
        check(actual == bytes, "bytes", &actual, bytes)?;
    }
    // Present-and-null asserts the SDK surfaces NO request id rather than throwing (D2).
    if let Some(request_id) = expected.get("requestId") {
        let actual = result.request_id.as_deref();
        let want = request_id.as_str();
        check(actual == want, "requestId", actual, want)?;
    }

    Ok(())
}

fn run_retry_case(given: &Value, expect: &Value) -> Result<(), Failed> {
    let mut call = given.clone();
    call["source"] = Value::from("zpl");
    call["target"] = Value::from("zpl");
    call["bodyText"] = Value::from("^XA^XZ");

    let responses = array(given, "responses");
    let outcome = execute(&call, responses, None)?;

    if let Some(expected) = expect.get("error") {
        assert_error(expected, outcome.error.as_ref())?;
    } else {
        if let Some(error) = &outcome.error {
            return Err(format!("unexpected error: {error}").into());
        }
        if let Some(text) = expect.pointer("/result/text").and_then(Value::as_str) {
            let result = outcome
                .result
                .as_ref()
                .expect("a successful case has a result");
            let actual = result.text();
            check(actual == text, "text", &actual, text)?;
        }
    }

    if let Some(attempts) = expect.get("attempts").and_then(Value::as_u64) {
        let actual = outcome.transport.count();
        check(actual as u64 == attempts, "attempts", actual, attempts)?;
    }
    let slept = outcome.sleeper.seconds();
    let want: Vec<f64> = array(expect, "sleepsSeconds")
        .iter()
        .filter_map(Value::as_f64)
        .collect();
    if slept.len() != want.len() || slept.iter().zip(&want).any(|(a, b)| (a - b).abs() > 1e-6) {
        return Err(format!("sleepsSeconds = {slept:?}, want {want:?}").into());
    }

    Ok(())
}

fn run_validation_case(given: &Value, expect: &Value) -> Result<(), Failed> {
    let ok_response = serde_json::json!({
        "status": 200,
        "headers": { "content-type": "text/plain" },
        "bodyText": "^XA^XZ"
    });
    let outcome = execute(given, vec![ok_response], None)?;

    match outcome.error {
        Some(Error::Validation(validation)) => {
            let want = expect
                .pointer("/validationError/parameter")
                .and_then(Value::as_str);
            check(
                Some(validation.parameter.as_str()) == want,
                "parameter",
                &validation.parameter,
                want.unwrap_or_default(),
            )?;
        }
        // A local rejection is not an API error, so an Error::Api arm implementing a
        // fallback must not swallow it.
        other => return Err(format!("expected Error::Validation, got {other:?}").into()),
    }

    if let Some(requests_sent) = expect.get("requestsSent").and_then(Value::as_u64) {
        let actual = outcome.transport.count();
        check(
            actual as u64 == requests_sent,
            "requests sent",
            actual,
            requests_sent,
        )?;
    }

    Ok(())
}

// ------------------------------------------------------------------------- print

/// One kind for the whole print surface (`CONFORMANCE.md`, "print"). `given.maxRetries`
/// defaults to 0, and `expect.request` describes the FIRST request.
fn run_print_case(given: &Value, expect: &Value) -> Result<(), Failed> {
    let harness = build_client(given, array(given, "responses"), Some(0))?;
    let outcome = run_print_operation(&harness.client, given)?;

    if let Some(validation) = expect.get("validationError") {
        match &outcome {
            Err(Error::Validation(error)) => {
                let want = validation["parameter"].as_str();
                check(
                    Some(error.parameter.as_str()) == want,
                    "parameter",
                    &error.parameter,
                    want.unwrap_or_default(),
                )?;
            }
            other => return Err(format!("expected Error::Validation, got {other:?}").into()),
        }
        let want = expect["requestsSent"].as_u64().unwrap_or(0);
        let actual = harness.transport.count();
        check(actual as u64 == want, "requests sent", actual, want)?;
        return Ok(());
    }

    if let Some(expected) = expect.get("error") {
        assert_error(expected, outcome.as_ref().err())?;
    } else {
        let actual = outcome.map_err(|error| format!("unexpected error: {error}"))?;
        let (name, expected) = expect["result"]
            .as_object()
            .and_then(|result| result.iter().next())
            .ok_or("expect.result names no result")?;
        assert_subset(expected, &actual, name)?;
    }

    if let Some(request) = expect.get("request") {
        let first = harness
            .transport
            .requests
            .lock()
            .unwrap()
            .first()
            .cloned()
            .ok_or("no request was sent")?;
        assert_request(&first, request)?;
    }

    if let Some(attempts) = expect.get("attempts").and_then(Value::as_u64) {
        let actual = harness.transport.count();
        check(actual as u64 == attempts, "attempts", actual, attempts)?;
    }
    if let Some(sleeps) = expect.get("sleepsSeconds").and_then(Value::as_array) {
        let slept = harness.sleeper.seconds();
        let want: Vec<f64> = sleeps.iter().filter_map(Value::as_f64).collect();
        if slept.len() != want.len() || slept.iter().zip(&want).any(|(a, b)| (a - b).abs() > 1e-6) {
            return Err(format!("sleepsSeconds = {slept:?}, want {want:?}").into());
        }
    }
    for header in array(expect, "sameHeaderAcrossAttempts") {
        let header = header.as_str().unwrap_or_default();
        let requests = harness.transport.requests.lock().unwrap();
        let values: Vec<Option<&str>> = requests
            .iter()
            .map(|request| request.header(header))
            .collect();
        if values.first().copied().flatten().is_none()
            || values.iter().any(|value| *value != values[0])
        {
            return Err(format!("header {header} differs across attempts: {values:?}").into());
        }
    }

    Ok(())
}

/// Runs the fixture's print call and projects its result into the fixture's naming, so
/// one subset comparison covers all four result types.
fn run_print_operation(
    client: &LabelZoomClient,
    given: &Value,
) -> Result<Result<Value, Error>, Failed> {
    let text = |name: &str| given[name].as_str().unwrap_or_default().to_owned();
    let copies = given
        .get("copies")
        .and_then(Value::as_u64)
        .map(|copies| u32::try_from(copies).unwrap_or(u32::MAX));
    let key = given
        .get("idempotencyKey")
        .and_then(Value::as_str)
        .map(str::to_owned);

    Ok(match given["operation"].as_str().unwrap_or_default() {
        "print" => {
            let source = SourceFormat::from_name(&text("source"))
                .ok_or_else(|| format!("unknown source '{}'", given["source"]))?;
            let mut request = PrintRequest::new(text("printerId"), source, text("bodyText"))
                .options(build_print_options(given)?);
            request.copies = copies;
            request.idempotency_key = key;
            client.print(&request).map(|job| project_job(&job))
        }
        "printTemplate" => {
            let mut request = TemplatePrintRequest::new(text("printerId"), text("templateId"));
            // Set as raw values, not through with_data: a non-object record has to reach
            // the client for validation-template-data-not-an-object to mean anything.
            request.data = array(given, "data");
            request.validate = given.get("validate").and_then(Value::as_bool) == Some(true);
            request.copies = copies;
            request.idempotency_key = key;
            client.print_template(&request).map(|batch| {
                serde_json::json!({
                    "jobs": batch.jobs.iter().map(project_job).collect::<Vec<_>>(),
                    "remaining": batch.remaining,
                    "message": batch.message,
                })
            })
        }
        "getJob" => client.get_print_job(&text("jobId")).map(|job| {
            serde_json::json!({
                "id": job.id,
                "printerId": job.printer_id,
                "agentId": job.agent_id,
                "sourceFormat": job.source_format,
                "copies": job.copies,
                "status": job.status,
                "error": job.error,
                "retryCount": job.retry_count,
                "createdDate": job.created_date,
                "dispatchedDate": job.dispatched_date,
                "completedDate": job.completed_date,
                "retriedDate": job.retried_date,
            })
        }),
        "getQuota" => client.get_print_quota().map(|quota| {
            let counter = |counter: &labelzoom::PrintQuotaCounter| {
                serde_json::json!({
                    "used": counter.used,
                    "limit": counter.limit,
                    "remaining": counter.remaining,
                    "pending": counter.pending,
                    "resetAt": counter.reset_at,
                })
            };
            serde_json::json!({
                "plan": quota.plan,
                "agents": counter(&quota.agents),
                "printers": counter(&quota.printers),
                "jobsToday": counter(&quota.jobs_today),
                "limits": {
                    "maxPayloadBytes": quota.limits.max_payload_bytes,
                    "maxStoredPayloadBytes": quota.limits.max_stored_payload_bytes,
                    "maxBatchSize": quota.limits.max_batch_size,
                },
            })
        }),
        other => return Err(format!("unknown print operation '{other}'").into()),
    })
}

fn project_job(job: &labelzoom::PrintJob) -> Value {
    serde_json::json!({
        "jobId": job.job_id,
        "status": job.status,
        "copies": job.copies,
        "idempotent": job.idempotent,
    })
}

/// Translates the fixture's wire-shaped source options onto `PrintOptions`, through its
/// public setters. Like `build_options`, an unmapped option fails the case rather than
/// being dropped.
fn build_print_options(given: &Value) -> Result<PrintOptions, Failed> {
    let mut options = PrintOptions::new();
    let Some(wire) = given.get("options").and_then(Value::as_object) else {
        return Ok(options);
    };

    for (key, value) in wire {
        let number = |name: &str| value.get(name).and_then(Value::as_f64);
        let whole = |name: &str| value.get(name).and_then(Value::as_i64);
        options = match key.as_str() {
            "sourceDpi" => options.source_dpi(value.as_i64().unwrap_or_default()),
            "labelIndex" => options.label_index(value.as_i64().unwrap_or_default()),
            "label" => options.label_size(
                number("width").ok_or("label.width is missing")?,
                number("height").ok_or("label.height is missing")?,
            ),
            "pdf" => {
                if let Some(page) = whole("pageNumber") {
                    options = options.pdf_page(page);
                }
                match value.get("conversionMode").and_then(Value::as_str) {
                    Some("IMAGE") => options.pdf_conversion_mode(PdfConversionMode::Image),
                    Some("NATIVE") => options.pdf_conversion_mode(PdfConversionMode::Native),
                    Some(other) => return Err(format!("unknown conversionMode '{other}'").into()),
                    None => options,
                }
            }
            "split" => {
                let unmapped: Vec<&String> = value
                    .as_object()
                    .into_iter()
                    .flat_map(Map::keys)
                    .filter(|name| !matches!(name.as_str(), "rows" | "columns"))
                    .collect();
                if !unmapped.is_empty() {
                    return Err(format!(
                        "the print runner maps split.rows and split.columns only, not {unmapped:?}"
                    )
                    .into());
                }
                options.split(
                    whole("rows").ok_or("split.rows is missing")?,
                    whole("columns").ok_or("split.columns is missing")?,
                )
            }
            other => {
                return Err(format!(
                    "Fixture sets print option '{other}', which the Rust runner does not map. \
                     Add it to build_print_options rather than skipping the case."
                )
                .into())
            }
        };
    }

    Ok(options)
}

/// Asserts every member the fixture gives, and only those. Timestamps compare as instants,
/// so `2026-10-11T15:00:00.000Z` matches `2026-10-11T15:00:00Z`; `null` means no value.
fn assert_subset(expected: &Value, actual: &Value, path: &str) -> Result<(), Failed> {
    match expected {
        Value::Object(members) => {
            for (name, value) in members {
                let field = format!("{path}.{name}");
                let actual = actual
                    .get(name)
                    .ok_or_else(|| format!("{field} is missing"))?;
                assert_subset(value, actual, &field)?;
            }
            Ok(())
        }
        Value::Array(items) => {
            let actual = actual
                .as_array()
                .ok_or_else(|| format!("{path}: expected an array, got {actual}"))?;
            check(
                items.len() == actual.len(),
                &format!("{path} length"),
                actual.len(),
                items.len(),
            )?;
            for (index, (item, other)) in items.iter().zip(actual).enumerate() {
                assert_subset(item, other, &format!("{path}[{index}]"))?;
            }
            Ok(())
        }
        Value::String(text) if instant::parse(text).is_some() => {
            let actual_instant = actual.as_str().and_then(instant::parse);
            check(
                actual_instant == instant::parse(text),
                path,
                actual,
                expected,
            )
        }
        _ => check(json_equivalent(actual, expected), path, actual, expected),
    }
}

/// RFC 3339 timestamps reduced to nanoseconds since the epoch, enough to compare two
/// spellings of one instant. The crate keeps timestamps as the server's strings, so the
/// runner is what decides that two of them are the same moment.
mod instant {
    pub fn parse(text: &str) -> Option<i128> {
        let (date, time) = text.split_once('T')?;
        let mut date_parts = date.splitn(3, '-');
        let year: i64 = date_parts.next()?.parse().ok()?;
        let month: i64 = date_parts.next()?.parse().ok()?;
        let day: i64 = date_parts.next()?.parse().ok()?;

        let (clock, offset_seconds) = if let Some(clock) = time.strip_suffix(['Z', 'z']) {
            (clock, 0)
        } else {
            let split = time.rfind(['+', '-'])?;
            let (clock, offset) = time.split_at(split);
            let sign = if offset.starts_with('-') { -1 } else { 1 };
            let (hours, minutes) = offset[1..].split_once(':')?;
            let offset = hours.parse::<i64>().ok()? * 3600 + minutes.parse::<i64>().ok()? * 60;
            (clock, sign * offset)
        };

        let (whole, fraction) = clock.split_once('.').unwrap_or((clock, ""));
        let mut clock_parts = whole.splitn(3, ':');
        let hour: i64 = clock_parts.next()?.parse().ok()?;
        let minute: i64 = clock_parts.next()?.parse().ok()?;
        let second: i64 = clock_parts.next()?.parse().ok()?;
        if !fraction.chars().all(|c| c.is_ascii_digit()) || fraction.len() > 9 {
            return None;
        }
        let nanos: i128 = format!("{fraction:0<9}").parse().ok()?;

        let seconds =
            days_from_civil(year, month, day) * 86_400 + hour * 3600 + minute * 60 + second
                - offset_seconds;
        Some(i128::from(seconds) * 1_000_000_000 + nanos)
    }

    /// Howard Hinnant's days-from-civil, proleptic Gregorian.
    fn days_from_civil(year: i64, month: i64, day: i64) -> i64 {
        let year = if month <= 2 { year - 1 } else { year };
        let era = year.div_euclid(400);
        let year_of_era = year - era * 400;
        let month_index = (month + 9) % 12;
        let day_of_year = (153 * month_index + 2) / 5 + day - 1;
        let day_of_era = year_of_era * 365 + year_of_era / 4 - year_of_era / 100 + day_of_year;
        era * 146_097 + day_of_era - 719_468
    }
}

// ---------------------------------------------------------------------- assertions

/// Maps `spec.json`'s symbolic kind names onto this crate's `ApiErrorKind` variants.
fn matches_kind(kind: &str, actual: &ApiErrorKind) -> bool {
    match kind {
        "BadRequest" => matches!(actual, ApiErrorKind::BadRequest),
        "Unauthorized" => matches!(actual, ApiErrorKind::Unauthorized),
        "Forbidden" => matches!(actual, ApiErrorKind::Forbidden { .. }),
        "NotFound" => matches!(actual, ApiErrorKind::NotFound),
        "PayloadTooLarge" => matches!(actual, ApiErrorKind::PayloadTooLarge),
        "Unprocessable" => matches!(actual, ApiErrorKind::Unprocessable),
        // PrintQuotaExceeded is a kind of RateLimited (rule E4), so a fixture naming the
        // general kind accepts the specific one, as a handler for the base type would.
        "RateLimited" => matches!(
            actual,
            ApiErrorKind::RateLimited { .. } | ApiErrorKind::PrintQuotaExceeded { .. }
        ),
        "PrintQuotaExceeded" => matches!(actual, ApiErrorKind::PrintQuotaExceeded { .. }),
        "ServerError" => matches!(actual, ApiErrorKind::ServerError),
        _ => false,
    }
}

fn assert_error(expected: &Value, actual: Option<&Error>) -> Result<(), Failed> {
    let api = actual
        .and_then(Error::as_api)
        .ok_or_else(|| format!("expected an API error, got {actual:?}"))?;

    if let Some(kind) = expected.get("kind").and_then(Value::as_str) {
        if !matches_kind(kind, &api.kind) {
            return Err(format!("error kind is {:?}, want {kind}", api.kind).into());
        }
    }
    if let Some(status) = expected.get("status").and_then(Value::as_u64) {
        check(
            u64::from(api.status) == status,
            "status",
            api.status,
            status,
        )?;
    }
    if let Some(message) = expected.get("message").and_then(Value::as_str) {
        check(api.message == message, "message", &api.message, message)?;
    }
    // Present-and-null asserts the SDK reports no code (rule E3).
    if let Some(code) = expected.get("code") {
        let actual = api.code.as_deref();
        check(actual == code.as_str(), "code", actual, code.as_str())?;
    }
    if expected.get("messageNonEmpty").and_then(Value::as_bool) == Some(true)
        && api.message.trim().is_empty()
    {
        return Err("message must not be empty".into());
    }
    if let Some(max) = expected.get("messageMaxLength").and_then(Value::as_u64) {
        let actual = api.message.chars().count();
        if actual as u64 > max {
            return Err(format!("message is {actual} characters, want at most {max}").into());
        }
    }
    if let Some(length) = expected.get("rawBodyLength").and_then(Value::as_u64) {
        let actual = api.raw_body.len();
        check(actual as u64 == length, "rawBody length", actual, length)?;
    }
    if expected.get("rawBodyPresent").and_then(Value::as_bool) == Some(true)
        && api.raw_body.is_empty()
    {
        return Err("rawBody must be present".into());
    }
    if let Some(request_id) = expected.get("requestId") {
        let actual = api.request_id.as_deref();
        check(
            actual == request_id.as_str(),
            "requestId",
            actual,
            request_id.as_str(),
        )?;
    }
    if let Some(paid) = expected.get("isPaidFeature").and_then(Value::as_bool) {
        check(
            api.is_paid_feature() == paid,
            "isPaidFeature",
            api.is_paid_feature(),
            paid,
        )?;
    }
    if let Some(seconds) = expected.get("retryAfterSeconds").and_then(Value::as_f64) {
        let actual = api
            .retry_after_seconds()
            .ok_or("retryAfterSeconds is absent")?;
        if (actual - seconds).abs() > 1e-6 {
            return Err(format!("retryAfterSeconds = {actual}, want {seconds}").into());
        }
    }

    Ok(())
}

/// Numeric-tolerant structural comparison: 4 and 4.0 must match, because languages
/// disagree about trailing zeros.
fn json_equivalent(actual: &Value, expected: &Value) -> bool {
    match (actual, expected) {
        (Value::Number(a), Value::Number(b)) => match (a.as_f64(), b.as_f64()) {
            (Some(a), Some(b)) => (a - b).abs() < 1e-9,
            _ => a == b,
        },
        (Value::Array(a), Value::Array(b)) => {
            a.len() == b.len() && a.iter().zip(b).all(|(a, b)| json_equivalent(a, b))
        }
        (Value::Object(a), Value::Object(b)) => {
            a.len() == b.len()
                && a.iter().all(|(key, value)| {
                    b.get(key)
                        .is_some_and(|other| json_equivalent(value, other))
                })
        }
        _ => actual == expected,
    }
}

fn check<A: std::fmt::Debug, E: std::fmt::Debug>(
    ok: bool,
    what: &str,
    actual: A,
    expected: E,
) -> Result<(), Failed> {
    if ok {
        Ok(())
    } else {
        Err(format!("{what} = {actual:?}, want {expected:?}").into())
    }
}

fn object(value: &Value, key: &str) -> Vec<(String, Value)> {
    value
        .get(key)
        .and_then(Value::as_object)
        .map(|map| map.iter().map(|(k, v)| (k.clone(), v.clone())).collect())
        .unwrap_or_default()
}

fn array(value: &Value, key: &str) -> Vec<Value> {
    value
        .get(key)
        .and_then(Value::as_array)
        .cloned()
        .unwrap_or_default()
}

/// The fixtures' header patterns are small regexes: anchors, literals, `[...]` classes and
/// `+`/`*`/`?`/`{n}`/`{n,m}` quantifiers. Matching that subset here avoids a regex
/// dependency for a handful of patterns, and anything outside it is an error rather than a
/// silent mismatch, so a fixture that grows a new construct fails loudly.
fn matches_pattern(value: &str, pattern: &str) -> Result<bool, Failed> {
    pattern::Pattern::parse(pattern)
        .map(|pattern| pattern.is_match(value))
        .map_err(|error| format!("header pattern {pattern:?}: {error}").into())
}

mod pattern {
    enum Atom {
        Literal(char),
        Class(Vec<(char, char)>),
    }

    struct Piece {
        atom: Atom,
        min: usize,
        max: usize,
    }

    pub struct Pattern {
        anchored_start: bool,
        anchored_end: bool,
        pieces: Vec<Piece>,
    }

    impl Pattern {
        pub fn parse(pattern: &str) -> Result<Self, String> {
            let mut chars: Vec<char> = pattern.chars().collect();
            let anchored_start = chars.first() == Some(&'^');
            if anchored_start {
                chars.remove(0);
            }
            let anchored_end = chars.last() == Some(&'$');
            if anchored_end {
                chars.pop();
            }

            let mut pieces = Vec::new();
            let mut index = 0;
            while index < chars.len() {
                let atom = match chars[index] {
                    '[' => {
                        let close = chars[index..]
                            .iter()
                            .position(|c| *c == ']')
                            .ok_or("unclosed character class")?
                            + index;
                        let body = &chars[index + 1..close];
                        if body.first() == Some(&'^') {
                            return Err("negated classes are not supported".into());
                        }
                        let mut ranges = Vec::new();
                        let mut at = 0;
                        while at < body.len() {
                            if at + 2 < body.len() && body[at + 1] == '-' {
                                ranges.push((body[at], body[at + 2]));
                                at += 3;
                            } else {
                                ranges.push((body[at], body[at]));
                                at += 1;
                            }
                        }
                        index = close + 1;
                        Atom::Class(ranges)
                    }
                    '\\' => {
                        let escaped = *chars.get(index + 1).ok_or("trailing backslash")?;
                        index += 2;
                        Atom::Literal(escaped)
                    }
                    '.' | '(' | ')' | '|' | '^' | '$' | '+' | '*' | '?' | '{' => {
                        return Err(format!("unsupported construct '{}'", chars[index]));
                    }
                    literal => {
                        index += 1;
                        Atom::Literal(literal)
                    }
                };

                let (min, max) = match chars.get(index) {
                    Some('+') => {
                        index += 1;
                        (1, usize::MAX)
                    }
                    Some('*') => {
                        index += 1;
                        (0, usize::MAX)
                    }
                    Some('?') => {
                        index += 1;
                        (0, 1)
                    }
                    Some('{') => {
                        let close = chars[index..]
                            .iter()
                            .position(|c| *c == '}')
                            .ok_or("unclosed quantifier")?
                            + index;
                        let body: String = chars[index + 1..close].iter().collect();
                        let bound = |text: &str| {
                            text.trim()
                                .parse::<usize>()
                                .map_err(|_| format!("bad quantifier {{{body}}}"))
                        };
                        index = close + 1;
                        match body.split_once(',') {
                            Some((low, high)) => (bound(low)?, bound(high)?),
                            None => {
                                let exact = bound(&body)?;
                                (exact, exact)
                            }
                        }
                    }
                    _ => (1, 1),
                };
                pieces.push(Piece { atom, min, max });
            }

            Ok(Self {
                anchored_start,
                anchored_end,
                pieces,
            })
        }

        pub fn is_match(&self, value: &str) -> bool {
            let chars: Vec<char> = value.chars().collect();
            let starts = if self.anchored_start {
                0..=0
            } else {
                0..=chars.len()
            };
            starts
                .into_iter()
                .any(|start| self.match_from(&self.pieces, &chars[start..]))
        }

        /// Greedy with backtracking: plenty for patterns this size.
        fn match_from(&self, pieces: &[Piece], rest: &[char]) -> bool {
            let Some((piece, others)) = pieces.split_first() else {
                return !self.anchored_end || rest.is_empty();
            };
            let available = rest
                .iter()
                .take(piece.max)
                .take_while(|c| piece.atom.accepts(**c))
                .count();
            (piece.min..=available)
                .rev()
                .any(|taken| self.match_from(others, &rest[taken..]))
        }
    }

    impl Atom {
        fn accepts(&self, c: char) -> bool {
            match self {
                Self::Literal(literal) => c == *literal,
                Self::Class(ranges) => ranges.iter().any(|(low, high)| (*low..=*high).contains(&c)),
            }
        }
    }
}

impl Outcome {
    fn last_request(&self) -> Result<labelzoom::HttpRequest, Failed> {
        self.transport
            .last()
            .ok_or_else(|| "no request was sent".into())
    }
}

/// A minimal URL splitter. The crate emits the URL, so this only has to read back what it
/// wrote -- pulling in a URL parser to check a string this suite produced would be
/// testing the parser.
mod url {
    use libtest_mimic::Failed;
    use std::collections::HashMap;

    pub struct Parts {
        pub origin_and_path: String,
        pub path: String,
        pub query: HashMap<String, String>,
    }

    impl Parts {
        pub fn parse(url: &str) -> Result<Self, Failed> {
            let (before_query, raw_query) = match url.split_once('?') {
                Some((before, query)) => (before, query),
                None => (url, ""),
            };
            let (scheme, rest) = before_query
                .split_once("://")
                .ok_or_else(|| format!("{url} has no scheme"))?;
            let (host, path) = match rest.split_once('/') {
                Some((host, path)) => (host, format!("/{path}")),
                None => (rest, String::new()),
            };

            let mut query = HashMap::new();
            for pair in raw_query.split('&').filter(|pair| !pair.is_empty()) {
                let (name, value) = pair.split_once('=').unwrap_or((pair, ""));
                query.insert(name.to_owned(), percent_decode(value)?);
            }

            Ok(Self {
                origin_and_path: format!("{scheme}://{host}{path}"),
                path,
                query,
            })
        }
    }

    fn percent_decode(value: &str) -> Result<String, Failed> {
        let bytes = value.as_bytes();
        let mut out = Vec::with_capacity(bytes.len());
        let mut index = 0;
        while index < bytes.len() {
            match bytes[index] {
                b'%' if index + 2 < bytes.len() => {
                    let hex = std::str::from_utf8(&bytes[index + 1..index + 3])?;
                    out.push(u8::from_str_radix(hex, 16)?);
                    index += 3;
                }
                b'+' => {
                    out.push(b' ');
                    index += 1;
                }
                byte => {
                    out.push(byte);
                    index += 1;
                }
            }
        }
        Ok(String::from_utf8(out)?)
    }
}
