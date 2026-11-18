//! Cloud Print: the request structs, the result types, and the print-only validation.
//!
//! A printer is a target. A print runs the convert pipeline in the same order: the source,
//! how it's read, then where it goes. The printer stands where a [`TargetFormat`] would, and
//! LabelZoom converts the document to the printer's configured language. So a print takes the
//! *source* options a conversion takes, plus rotation, which turns the finished label for
//! every format. It takes no other target option, because the rest depend on an output
//! language the request doesn't name.
//!
//! [`TargetFormat`]: crate::TargetFormat

use crate::error::Error;
use crate::formats::SourceFormat;
use crate::options::{ColorMode, ConversionOptions, PdfConversionMode};
use serde_json::{Map, Value};

const MIN_COPIES: u32 = 1;
const MAX_COPIES: u32 = 9999;

/// The options a print takes: how the document is read, and its rotation.
///
/// These are the source options of [`ConversionOptions`] plus [`rotation`](Self::rotation),
/// the one target option a printer takes. The other target options depend on an output
/// language, and the printer's configuration picks that. Each method here is the [`ConversionOptions`] method of the same name, applied to the same
/// `?params=<JSON>` object, so a print and a conversion validate and serialize an option
/// identically. Only the options you set are sent, and setting none sends no `params` at
/// all, which prints a document already in the printer's language as is.
#[derive(Clone, Debug, Default, PartialEq)]
pub struct PrintOptions {
    options: ConversionOptions,
}

impl PrintOptions {
    /// Fresh options with nothing set.
    #[must_use]
    pub fn new() -> Self {
        Self::default()
    }

    /// Sets the resolution the document was made for. Server default 203. Not for a source
    /// with no dots, such as a PDF or LabelZoom XML.
    #[must_use]
    pub fn source_dpi(self, dpi: i64) -> Self {
        self.with(|options| options.source_dpi(dpi))
    }

    /// Turns the finished label, in degrees clockwise. Must be a multiple of 90. This is the
    /// printer target's one option, sent in `params` with the source options.
    #[must_use]
    pub fn rotation(self, degrees: i64) -> Self {
        self.with(|options| options.rotation(degrees))
    }

    /// Image or PDF sources: sets the scaling percentage. Server default 100.
    #[must_use]
    pub fn scaling(self, percent: f64) -> Self {
        self.with(|options| options.scaling(percent))
    }

    /// Image or PDF sources: sets how the picture is reduced to black and white.
    #[must_use]
    pub fn color_mode(self, mode: ColorMode) -> Self {
        self.with(|options| options.color_mode(mode))
    }

    /// Image or PDF sources: sets the luminance threshold, 0 to 100.
    #[must_use]
    pub fn darkness(self, darkness: i64) -> Self {
        self.with(|options| options.darkness(darkness))
    }

    /// Image or PDF sources: places the picture on the label, in dots. A negative offset
    /// crops it.
    #[must_use]
    pub fn position(self, x: i64, y: i64) -> Self {
        self.with(|options| options.position(x, y))
    }

    /// Sets the media size, in **inches**. Leave it unset to have the server detect it.
    #[must_use]
    pub fn label_size(self, width_inches: f64, height_inches: f64) -> Self {
        self.with(|options| options.label_size(width_inches, height_inches))
    }

    /// Prints only this label. **Zero-based**; for an image or PDF it counts the labels after
    /// [`split`](Self::split), page by page.
    #[must_use]
    pub fn label_index(self, zero_based_index: i64) -> Self {
        self.with(|options| options.label_index(zero_based_index))
    }

    /// Sets the variable-field values, one record per printed label.
    #[must_use]
    pub fn with_data<I>(self, records: I) -> Self
    where
        I: IntoIterator<Item = Map<String, Value>>,
    {
        self.with(|options| options.with_data(records))
    }

    /// Sets how a PDF source is read.
    #[must_use]
    pub fn pdf_conversion_mode(self, mode: PdfConversionMode) -> Self {
        self.with(|options| options.pdf_conversion_mode(mode))
    }

    /// Prints a single PDF page. **Zero-based**; omit it to print every page.
    #[must_use]
    pub fn pdf_page(self, zero_based_page_number: i64) -> Self {
        self.with(|options| options.pdf_page(zero_based_page_number))
    }

    /// ZPL sources: skips the named commands while reading, such as `^PQ`.
    #[must_use]
    pub fn zpl_commands_to_ignore<I, S>(self, commands: I) -> Self
    where
        I: IntoIterator<Item = S>,
        S: Into<String>,
    {
        self.with(|options| options.zpl_commands_to_ignore(commands))
    }

    /// Image or PDF sources: cuts each page into `rows` x `columns` labels, read left to
    /// right and then down.
    #[must_use]
    pub fn split(self, rows: i64, columns: i64) -> Self {
        self.with(|options| options.split(rows, columns))
    }

    /// Sets the size of one label on the source page, in **inches**.
    #[must_use]
    pub fn split_label_size(self, width_inches: f64, height_inches: f64) -> Self {
        self.with(|options| options.split_label_size(width_inches, height_inches))
    }

    /// Sets the distance from the page's left and top edges to the first label, in
    /// **inches**.
    #[must_use]
    pub fn split_margins(self, left_inches: f64, top_inches: f64) -> Self {
        self.with(|options| options.split_margins(left_inches, top_inches))
    }

    /// Sets the space between two columns and between two rows of labels, in **inches**.
    #[must_use]
    pub fn split_gaps(self, column_gap_inches: f64, row_gap_inches: f64) -> Self {
        self.with(|options| options.split_gaps(column_gap_inches, row_gap_inches))
    }

    /// Leaves out labels with nothing printable.
    #[must_use]
    pub fn split_skip_blank(self, skip_blank: bool) -> Self {
        self.with(|options| options.split_skip_blank(skip_blank))
    }

    /// Adds a parameter this crate does not model. Unknown keys are ignored server-side.
    #[must_use]
    pub fn extra(self, key: impl Into<String>, value: impl Into<Value>) -> Self {
        self.with(|options| options.extra(key, value))
    }

    /// The options as the conversion parameters they are sent as.
    pub fn as_conversion_options(&self) -> &ConversionOptions {
        &self.options
    }

    /// Every setter is a one-line call into the [`ConversionOptions`] writer, so the two
    /// cannot disagree on validation or on the JSON path.
    fn with(mut self, write: impl FnOnce(ConversionOptions) -> ConversionOptions) -> Self {
        self.options = write(self.options);
        self
    }
}

/// One document print.
///
/// Build it with [`PrintRequest::new`] and the setters, then pass it to
/// [`LabelZoomClient::print`](crate::LabelZoomClient::print). The fields read in pipeline
/// order, as a [`ConvertRequest`](crate::ConvertRequest) does: the source, how it's read, then
/// the printer in the target's place.
#[derive(Clone, Debug)]
#[non_exhaustive]
pub struct PrintRequest {
    /// The format of [`body`](PrintRequest::body). Every format prints except
    /// [`SourceFormat::Url`]; see [`is_printable`](SourceFormat::is_printable).
    pub source: SourceFormat,
    /// The document, as the format's own bytes. Print takes no base64 text.
    pub body: Vec<u8>,
    /// How the document is read, and its rotation.
    pub options: PrintOptions,
    /// The printer's id, from the Printers page or the printers list.
    pub printer_id: String,
    /// Sends the whole job this many times, 1 to 9999. It multiplies any quantity in the
    /// document: `^PQ3` with 5 copies prints 15 labels.
    pub copies: Option<u32>,
    /// Makes the print safe to repeat: the same key returns the original job instead of
    /// printing again. Without one, the client generates a key per call, so its own retries
    /// can't print twice.
    pub idempotency_key: Option<String>,
}

impl PrintRequest {
    /// A print of `body`, read as `source`, to `printer_id`, with nothing else set.
    #[must_use]
    pub fn new(
        source: SourceFormat,
        body: impl Into<Vec<u8>>,
        printer_id: impl Into<String>,
    ) -> Self {
        Self {
            source,
            body: body.into(),
            options: PrintOptions::new(),
            printer_id: printer_id.into(),
            copies: None,
            idempotency_key: None,
        }
    }

    /// Replaces the options.
    #[must_use]
    pub fn options(mut self, options: PrintOptions) -> Self {
        self.options = options;
        self
    }

    /// Sends the whole job this many times, 1 to 9999.
    #[must_use]
    pub fn copies(mut self, copies: u32) -> Self {
        self.copies = Some(copies);
        self
    }

    /// Sets the `Idempotency-Key`. Use something unique to the label, such as an order
    /// number; a deliberate reprint needs a new key.
    #[must_use]
    pub fn idempotency_key(mut self, key: impl Into<String>) -> Self {
        self.idempotency_key = Some(key.into());
        self
    }
}

/// One print of a template published from LabelZoom Studio or the web designer.
///
/// Build it with [`TemplatePrintRequest::new`] and the setters, then pass it to
/// [`LabelZoomClient::print_template`](crate::LabelZoomClient::print_template). A template is
/// a source of its own: it is already a label, so it takes merge data instead of source
/// options, and a printer is its only target.
#[derive(Clone, Debug)]
#[non_exhaustive]
pub struct TemplatePrintRequest {
    /// The published template's id.
    pub template_id: String,
    /// The merge-field values, one record per label. Leave it empty for a template with no
    /// merge fields.
    ///
    /// Typed as `Vec<Value>` so that a non-object record is expressible and is rejected with
    /// a [`ValidationError`](crate::ValidationError) rather than sent. Use
    /// [`TemplatePrintRequest::with_data`] to build it from records.
    pub data: Vec<Value>,
    /// Checks every record for missing merge fields before anything prints.
    pub validate: bool,
    /// The printer's id.
    pub printer_id: String,
    /// Sends each label this many times, 1 to 9999.
    pub copies: Option<u32>,
    /// One key for the whole batch: the server derives each record's key from it.
    pub idempotency_key: Option<String>,
}

impl TemplatePrintRequest {
    /// A print of `template_id` to `printer_id`, with no records.
    #[must_use]
    pub fn new(template_id: impl Into<String>, printer_id: impl Into<String>) -> Self {
        Self {
            template_id: template_id.into(),
            data: Vec::new(),
            validate: false,
            printer_id: printer_id.into(),
            copies: None,
            idempotency_key: None,
        }
    }

    /// Sets the merge-field values, one record per label.
    #[must_use]
    pub fn with_data<I>(mut self, records: I) -> Self
    where
        I: IntoIterator<Item = Map<String, Value>>,
    {
        self.data = records.into_iter().map(Value::Object).collect();
        self
    }

    /// Checks every record for missing merge fields before anything prints. The server
    /// answers a missing field with a 422.
    #[must_use]
    pub fn validate(mut self, validate: bool) -> Self {
        self.validate = validate;
        self
    }

    /// Sends each label this many times, 1 to 9999.
    #[must_use]
    pub fn copies(mut self, copies: u32) -> Self {
        self.copies = Some(copies);
        self
    }

    /// Sets the `Idempotency-Key` for the whole batch.
    #[must_use]
    pub fn idempotency_key(mut self, key: impl Into<String>) -> Self {
        self.idempotency_key = Some(key.into());
        self
    }
}

// ------------------------------------------------------------------------- results

/// A print job the server accepted.
#[derive(Clone, Debug, PartialEq, Eq)]
#[non_exhaustive]
pub struct PrintJob {
    /// The job's id. Pass it to
    /// [`LabelZoomClient::get_print_job`](crate::LabelZoomClient::get_print_job) to follow it.
    pub job_id: String,
    /// `dispatched` when an online agent took the job, or `queued` when none is online and it
    /// prints when one reconnects. Kept as the server's string, since statuses can be added.
    pub status: String,
    /// The copies requested, or `None` when the request didn't set any.
    pub copies: Option<u32>,
    /// True when the request reused an `Idempotency-Key`: this is the original job, and
    /// nothing printed again.
    pub idempotent: bool,
}

/// The jobs a template print created, one per record.
#[derive(Clone, Debug, PartialEq, Eq)]
#[non_exhaustive]
pub struct PrintBatch {
    /// The jobs submitted, in record order.
    pub jobs: Vec<PrintJob>,
    /// How many records at the end of the batch weren't submitted. Send the same request
    /// with the same `Idempotency-Key` to submit them: the records already submitted replay
    /// without printing again.
    pub remaining: u32,
    /// The server's explanation when [`remaining`](PrintBatch::remaining) isn't 0.
    pub message: Option<String>,
}

impl PrintBatch {
    /// Whether every record was submitted.
    pub fn is_complete(&self) -> bool {
        self.remaining == 0
    }
}

/// A print job's current state.
///
/// The four timestamps are RFC 3339 strings in UTC, exactly as the server sent them, such as
/// `2026-10-11T15:00:00.000Z`. They stay strings because this crate takes no date-time
/// dependency; parse them with the one your application already uses.
#[derive(Clone, Debug, PartialEq, Eq)]
#[non_exhaustive]
pub struct PrintJobDetails {
    /// The job's id.
    pub id: String,
    /// The printer the job was sent to.
    pub printer_id: Option<String>,
    /// The print agent that took the job, once one has.
    pub agent_id: Option<String>,
    /// The format the job was stored in: the printer's language, after any conversion.
    pub source_format: Option<String>,
    /// The copies requested, or `None` when the request didn't set any.
    pub copies: Option<u32>,
    /// `queued`, `dispatched`, `printing`, `completed` or `failed`. Kept as the server's
    /// string, since statuses can be added.
    pub status: String,
    /// Why the job failed, or `None`.
    pub error: Option<String>,
    /// How many times the job was retried.
    pub retry_count: u32,
    /// When the job was created.
    pub created_date: Option<String>,
    /// When an agent took the job.
    pub dispatched_date: Option<String>,
    /// When the job finished.
    pub completed_date: Option<String>,
    /// When the job was last retried.
    pub retried_date: Option<String>,
}

/// The account's Cloud Print limits and today's usage.
#[derive(Clone, Debug, PartialEq, Eq)]
#[non_exhaustive]
pub struct PrintQuota {
    /// The plan the limits come from.
    pub plan: String,
    /// Print agents. [`pending`](PrintQuotaCounter::pending) counts agents not yet approved.
    pub agents: PrintQuotaCounter,
    /// Printers.
    pub printers: PrintQuotaCounter,
    /// Print jobs today. Resets at [`reset_at`](PrintQuotaCounter::reset_at), 00:00 UTC.
    pub jobs_today: PrintQuotaCounter,
    /// Size limits.
    pub limits: PrintQuotaLimits,
}

/// One counted quota.
#[derive(Clone, Debug, PartialEq, Eq)]
#[non_exhaustive]
pub struct PrintQuotaCounter {
    /// How many are used.
    pub used: u64,
    /// The plan's limit.
    pub limit: u64,
    /// How many are left.
    pub remaining: u64,
    /// Agents only: how many are waiting for approval.
    pub pending: Option<u64>,
    /// Jobs today only: when the count resets, as an RFC 3339 string in UTC.
    pub reset_at: Option<String>,
}

/// The size limits on a print.
#[derive(Clone, Debug, PartialEq, Eq)]
#[non_exhaustive]
pub struct PrintQuotaLimits {
    /// The largest document a print accepts, in bytes.
    pub max_payload_bytes: u64,
    /// The largest job the server stores after conversion, in bytes.
    pub max_stored_payload_bytes: u64,
    /// The most records one template print accepts.
    pub max_batch_size: u64,
}

// ----------------------------------------------------------------------- validation

/// `printerId`, `templateId` and `jobId` must be non-empty: an empty one would address a
/// different endpoint (rule P7).
pub(crate) fn require_id(id: &str, parameter: &str) -> Result<(), Error> {
    if id.trim().is_empty() {
        return Err(Error::validation(
            parameter,
            format!("{parameter} cannot be empty."),
        ));
    }
    Ok(())
}

pub(crate) fn require_printable(source: SourceFormat) -> Result<(), Error> {
    if source.is_printable() {
        return Ok(());
    }
    Err(Error::validation(
        "source",
        "A URL can't be printed, because print doesn't fetch. Fetch the document yourself and \
         print its bytes as their own format.",
    ))
}

pub(crate) fn check_copies(copies: Option<u32>) -> Result<(), Error> {
    match copies {
        Some(copies) if !(MIN_COPIES..=MAX_COPIES).contains(&copies) => Err(Error::validation(
            "copies",
            format!("copies must be a whole number from 1 to 9999, but was {copies}."),
        )),
        _ => Ok(()),
    }
}

/// The key for one call, fixed before the first attempt so that every retry sends it
/// (rule P4). A caller's key is sent exactly as given, but never blank.
pub(crate) fn key_for_call(caller_key: Option<&str>) -> Result<String, Error> {
    match caller_key {
        Some(key) if key.trim().is_empty() => Err(Error::validation(
            "idempotencyKey",
            "An Idempotency-Key cannot be blank. Omit it to have the client generate one.",
        )),
        Some(key) => Ok(key.to_owned()),
        None => Ok(random_uuid_v4()),
    }
}

/// The template body: one record as that object, several as an array, none as `{}` (P3).
pub(crate) fn template_body(records: &[Value]) -> Result<Vec<u8>, Error> {
    for (index, record) in records.iter().enumerate() {
        if !record.is_object() {
            return Err(Error::validation(
                "data",
                format!(
                    "data[{index}] is not an object; every record must be an object whose keys \
                     are the template's merge field names."
                ),
            ));
        }
    }
    let body = match records {
        [] => Value::Object(Map::new()),
        [record] => record.clone(),
        several => Value::Array(several.to_vec()),
    };
    Ok(body.to_string().into_bytes())
}

/// A random, lowercase UUID v4.
///
/// Built from the standard library rather than a dependency. `RandomState` is `SipHash` keyed
/// from the operating system's random source, once per thread, with the key advanced on
/// every `RandomState::new`, so each half below is an unpredictable 64-bit value. A process
/// counter makes two calls in the same instant hash different input as well. The
/// `getrandom` crate would give the same result, but it would bring a dependency back into
/// the `default-features = false` build, which today has none beyond serde and
/// `encoding_rs`.
fn random_uuid_v4() -> String {
    use std::collections::hash_map::RandomState;
    use std::hash::{BuildHasher, Hasher};
    use std::sync::atomic::{AtomicU64, Ordering};

    static CALLS: AtomicU64 = AtomicU64::new(0);
    let call = CALLS.fetch_add(1, Ordering::Relaxed);

    let mut bytes = [0u8; 16];
    for (half, chunk) in bytes.chunks_mut(8).enumerate() {
        let mut hasher = RandomState::new().build_hasher();
        hasher.write_u64(call);
        hasher.write_usize(half);
        chunk.copy_from_slice(&hasher.finish().to_le_bytes());
    }
    // RFC 9562: version 4 in the high nibble of byte 6, variant 10xx in byte 8.
    bytes[6] = (bytes[6] & 0x0f) | 0x40;
    bytes[8] = (bytes[8] & 0x3f) | 0x80;

    let hex = bytes
        .iter()
        .fold(String::with_capacity(32), |mut hex, byte| {
            use std::fmt::Write as _;
            let _ = write!(hex, "{byte:02x}");
            hex
        });
    format!(
        "{}-{}-{}-{}-{}",
        &hex[0..8],
        &hex[8..12],
        &hex[12..16],
        &hex[16..20],
        &hex[20..32]
    )
}

// ------------------------------------------------------------------------ responses
//
// Read leniently, member by member, off a parsed value: an unknown member is ignored and a
// missing or mistyped one falls back to its "no value" reading, so a response the server
// extends later never fails the call (rule P6).

fn string(json: &Value, name: &str) -> Option<String> {
    json.get(name).and_then(Value::as_str).map(str::to_owned)
}

fn unsigned(json: &Value, name: &str) -> Option<u64> {
    json.get(name).and_then(Value::as_u64)
}

fn small(json: &Value, name: &str) -> Option<u32> {
    unsigned(json, name).and_then(|value| u32::try_from(value).ok())
}

pub(crate) fn read_print_job(json: &Value) -> PrintJob {
    PrintJob {
        job_id: string(json, "jobId").unwrap_or_default(),
        status: string(json, "status").unwrap_or_default(),
        copies: small(json, "copies"),
        idempotent: json.get("idempotent").and_then(Value::as_bool) == Some(true),
    }
}

/// One record comes back as a bare job, several as `{"jobs": [...]}`. Callers get one shape.
pub(crate) fn read_print_batch(json: &Value) -> PrintBatch {
    match json.get("jobs").and_then(Value::as_array) {
        Some(jobs) => PrintBatch {
            jobs: jobs.iter().map(read_print_job).collect(),
            remaining: small(json, "remaining").unwrap_or(0),
            message: string(json, "message"),
        },
        None => PrintBatch {
            jobs: vec![read_print_job(json)],
            remaining: 0,
            message: None,
        },
    }
}

/// The wire is `snake_case`.
pub(crate) fn read_print_job_details(json: &Value) -> PrintJobDetails {
    PrintJobDetails {
        id: string(json, "id").unwrap_or_default(),
        printer_id: string(json, "printer_id"),
        agent_id: string(json, "agent_id"),
        source_format: string(json, "source_format"),
        copies: small(json, "copies"),
        status: string(json, "status").unwrap_or_default(),
        error: string(json, "error"),
        retry_count: small(json, "retry_count").unwrap_or(0),
        created_date: string(json, "created_date"),
        dispatched_date: string(json, "dispatched_date"),
        completed_date: string(json, "completed_date"),
        retried_date: string(json, "retried_date"),
    }
}

pub(crate) fn read_print_quota(json: &Value) -> PrintQuota {
    let counter = |name: &str| {
        let group = json.get(name).unwrap_or(&Value::Null);
        PrintQuotaCounter {
            used: unsigned(group, "used").unwrap_or(0),
            limit: unsigned(group, "limit").unwrap_or(0),
            remaining: unsigned(group, "remaining").unwrap_or(0),
            pending: unsigned(group, "pending"),
            reset_at: string(group, "resetAt"),
        }
    };
    let limits = json.get("limits").unwrap_or(&Value::Null);
    PrintQuota {
        plan: string(json, "plan").unwrap_or_default(),
        agents: counter("agents"),
        printers: counter("printers"),
        jobs_today: counter("jobsToday"),
        limits: PrintQuotaLimits {
            max_payload_bytes: unsigned(limits, "maxPayloadBytes").unwrap_or(0),
            max_stored_payload_bytes: unsigned(limits, "maxStoredPayloadBytes").unwrap_or(0),
            max_batch_size: unsigned(limits, "maxBatchSize").unwrap_or(0),
        },
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn generates_lowercase_uuid_v4_keys() {
        let key = random_uuid_v4();
        let groups: Vec<&str> = key.split('-').collect();
        assert_eq!(
            groups.iter().map(|group| group.len()).collect::<Vec<_>>(),
            [8, 4, 4, 4, 12]
        );
        assert!(key
            .chars()
            .all(|c| c == '-' || c.is_ascii_digit() || ('a'..='f').contains(&c)));
        assert!(groups[2].starts_with('4'), "version nibble in {key}");
        assert!(
            matches!(groups[3].chars().next(), Some('8' | '9' | 'a' | 'b')),
            "variant bits in {key}"
        );
    }

    #[test]
    fn never_repeats_a_generated_key() {
        let keys: std::collections::HashSet<String> =
            (0..10_000).map(|_| random_uuid_v4()).collect();
        assert_eq!(keys.len(), 10_000);
    }

    #[test]
    fn template_body_shapes_records() {
        let record = |sku: &str| serde_json::json!({ "sku": sku });
        assert_eq!(template_body(&[]).unwrap(), b"{}");
        assert_eq!(template_body(&[record("A")]).unwrap(), br#"{"sku":"A"}"#);
        assert_eq!(
            template_body(&[record("A"), record("B")]).unwrap(),
            br#"[{"sku":"A"},{"sku":"B"}]"#
        );
    }

    #[test]
    fn a_single_object_response_is_a_batch_of_one() {
        let batch = read_print_batch(&serde_json::json!({ "jobId": "j", "status": "queued" }));
        assert_eq!(batch.jobs.len(), 1);
        assert!(batch.is_complete());
        assert_eq!(batch.jobs[0].copies, None);
        assert!(!batch.jobs[0].idempotent);
    }
}
