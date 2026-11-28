![LabelZoom Logo](../docs/LabelZoom_Logo_f_400px.png)

# LabelZoom Rust SDK

The `labelzoom` crate is the official Rust client for the [LabelZoom API](https://api.labelzoom.com).
Use it to convert labels between printer languages, PDF, images, and label design files, and to
print them on printers connected through the LabelZoom Print Agent.

## What the SDK handles

- Retries `429` responses, `5xx` responses, and transport failures with backoff, and honors a
  longer `Retry-After`.
- Returns typed errors that carry the HTTP status, the error `code`, the raw body, and the
  request ID.
- Returns every result as bytes, so PDF, image, and printer-language graphics arrive intact.
- Sends an `Idempotency-Key` with every print, so a retry can't print a label twice.
- Checks rotation, darkness, resolution, copies, and ids locally, before it sends a request.
- Works without an API key. Without one, the API serves the watermarked free tier. See
  [Rate limits and plans](https://docs.labelzoom.com/reference/rate-limits-and-plans/).
- Blocks the calling thread, with a bundled `ureq` and `rustls` HTTP stack: no async runtime, and
  no OpenSSL headers to build against.

## Install

The crate needs Rust 1.85 or later.

```sh
cargo add labelzoom
```

Or add it to `Cargo.toml`:

```toml
[dependencies]
labelzoom = "2"
```

The examples that build merge-field records also use `serde_json`. Add it with
`cargo add serde_json`.

<details>
<summary>Bring your own HTTP stack</summary>

```toml
labelzoom = { version = "2", default-features = false }
```

This build drops `ureq`, `rustls`, and `ring`, and you supply a
[`Transport`](#test-your-own-code) with `ClientBuilder::transport`. Without one,
`ClientBuilder::build` returns `Error::Validation` and `LabelZoomClient::new` panics.
</details>

## Convert a label

A conversion is a pipeline: choose the source, say how it's read, choose the target, say what's
written, then run it. This program converts page 0 of a PDF to ZPL for a 203 dpi printer:

```rust
use labelzoom::{ConversionOptions, ConvertRequest, LabelZoomClient, SourceFormat, TargetFormat};

fn main() -> Result<(), Box<dyn std::error::Error>> {
    let client = LabelZoomClient::new(); // reads LABELZOOM_API_KEY; works without one

    let pdf = std::fs::read("shipping-label.pdf")?;
    let request = ConvertRequest::new(SourceFormat::Pdf, TargetFormat::Zpl, pdf).options(
        ConversionOptions::new()
            // Source options: how the PDF is read.
            .pdf_page(0) // 0-based
            .label_size(4.0, 6.0) // inches
            // Target options: what's written.
            .target_dpi(203),
    );

    let result = client.convert(&request)?;
    result.save("label.zpl")?;
    Ok(())
}
```

`ConvertRequest::new` takes the source format, the target format, and the document.
`ConversionOptions` holds both kinds of option: source options change how the document is read,
and target options change what's written. [Options by stage](#options-by-stage) lists which is
which, and the formats each one applies to.

The SDK sends only the options you set and fills in no defaults of its own, so an option you leave
unset takes the server's default. With no options set, the request has no query string.

### Configure the client

`LabelZoomClient::new()` reads the API key from the `LABELZOOM_API_KEY` environment variable. To
set anything else, use the builder:

```rust
use labelzoom::LabelZoomClient;
use std::time::Duration;

let client = LabelZoomClient::builder()
    .api_key("API_KEY")
    .base_url("https://proxy.example.com/labelzoom")
    .timeout(Duration::from_secs(30))
    .user_agent_suffix("my-app/1.0")
    .build()?;
```

| Builder method | What it does |
| --- | --- |
| `api_key(key)` | Sets the credential: an `lz_live_` or `lz_test_` key, or a JWT. An empty string forces the free tier and ignores `LABELZOOM_API_KEY`. |
| `anonymous()` | Forces the free tier and ignores `LABELZOOM_API_KEY`. |
| `base_url(url)` | Overrides the API host. A path prefix is kept, so a reverse proxy at a subpath works. Default `https://api.labelzoom.com`. |
| `timeout(duration)` | Sets a per-request timeout. Only the bundled transport honors it. |
| `user_agent_suffix(token)` | Appends a token to the SDK's `User-Agent`. |
| `max_retries(n)` | Sets the retries after the first attempt. See [Retries](#retries). |

Without a key, the API serves the free tier: watermarked output, one label per request (the first,
or the one `label_index` selects), and no multi-page, JSON-target, or image-to-image conversion.
`client.is_authenticated()` tells you whether the client resolved a key. `LabelZoomClient` is
`Clone`, `Send`, and `Sync`, so you can build one and share it across threads.

## Print a label

A printer is a target. You name the source and the printer, and LabelZoom converts the document
to the printer's configured language, so a PDF prints on a ZPL printer. Printing needs an API key
with the `print` scope.

```rust
use labelzoom::{LabelZoomClient, PrintOptions, PrintRequest, SourceFormat, TemplatePrintRequest};
use serde_json::{json, Map, Value};
use std::time::Duration;

let client = LabelZoomClient::builder().api_key("API_KEY").build()?;

// Print a document: the source, how it's read, then the printer.
let job = client.print(
    &PrintRequest::new(SourceFormat::Pdf, std::fs::read("label.pdf")?, "PRINTER_ID")
        .options(PrintOptions::new().pdf_page(0).label_size(4.0, 6.0).rotation(90))
        .copies(2)
        .idempotency_key("SO-1001"),
)?;
println!("job {} is {}", job.job_id, job.status); // dispatched or queued

// Print a published template, one label per record.
let record: Map<String, Value> =
    serde_json::from_value(json!({ "orderNumber": "SO-1001", "sku": "A-100" }))?;
let batch = client.print_template(
    &TemplatePrintRequest::new("TEMPLATE_ID", "PRINTER_ID")
        .with_data([record])
        .validate(true)
        .copies(1)
        .idempotency_key("SO-1001-labels"),
)?;
println!("{} jobs, {} records not submitted", batch.jobs.len(), batch.remaining);

// Follow the document job until it finishes.
loop {
    let details = client.get_print_job(&job.job_id)?;
    match details.status.as_str() {
        "completed" | "failed" => break,
        _ => std::thread::sleep(Duration::from_secs(2)),
    }
}

// Check how many jobs the account has left today.
let quota = client.get_print_quota()?;
println!("{} of {} jobs left today", quota.jobs_today.remaining, quota.jobs_today.limit);
```

Every source format prints except `SourceFormat::Url`, because print doesn't fetch: download the
document and print its bytes in their own format. Print takes no base64 text either, so send the
document as its own bytes. `SourceFormat::is_printable` tells you whether a format prints.

`PrintOptions` takes the source options of `ConversionOptions`, such as `source_dpi`, `label_size`,
and `split`, plus `rotation`, which turns the finished label in degrees clockwise. It takes no other
target option, because the printer's configuration picks the output language. A template is
already a label, so `TemplatePrintRequest` takes merge data instead of options.

| Fact | Detail |
| --- | --- |
| Copies | `copies` sends the whole job 1 to 9999 times. It multiplies any quantity in the document, so `^PQ3` with 5 copies prints 15 labels. |
| Idempotency keys | Every print carries an `Idempotency-Key`: yours, or one the client generates for the call and reuses on its retries. A repeated request returns the original job, with `job.idempotent` set to `true`, instead of printing again. A deliberate reprint needs a new key. |
| Template batches | One key covers the whole batch. If `batch.remaining` isn't 0, send the same request with the same key to submit the rest; the records already submitted don't print again. `batch.message` says why. |
| Validation | `validate(true)` checks every record for missing merge fields before anything prints. A missing field returns `422`. |
| Job status | `PrintJob::status` is `dispatched` when an online agent took the job, or `queued` until one connects. `PrintJobDetails::status` is `queued`, `dispatched`, `printing`, `completed`, or `failed`. Both are strings, so a new status doesn't fail the call. |
| Timestamps | The `created_date`, `dispatched_date`, `completed_date`, and `retried_date` fields of `PrintJobDetails` are RFC 3339 strings in UTC. Parse them with your own date-time crate. |
| Daily limit | When the account's daily job limit is used up, a print fails with `ApiErrorKind::PrintQuotaExceeded`. The client doesn't retry it, because the limit resets at 00:00 UTC. Call `client.get_print_quota()` to check the jobs left. |

For the endpoints and limits behind these calls, see the
[Cloud Print API](https://docs.labelzoom.com/reference/cloud-print-api/).

## Formats

Sources: `Zpl` `Epl` `Ipl` `Tspl` `Dpl` `Sbpl` `EscLabel` `Fingerprint` `Ezpl` `Cpcl` `Tpcl`
`Nlbl` `Lbl` `Btw` `Dymo` `Xml` `Json` `Pdf` `Png` `Bmp` `Gif` `Jpeg` `Jpg` `Url`

Targets: `Zpl` `Epl` `Ipl` `Tspl` `Dpl` `Sbpl` `Cpcl` `Xml` `Json` `Pdf` `Png` `Bmp` `Gif` `Jpeg`

- ESC/Label, Fingerprint, EZPL, TPCL, and the design-file formats (NiceLabel, BarTender, and DYMO)
  are sources only. Send NiceLabel and BarTender files as bytes.
- `SourceFormat` and `TargetFormat` are separate enums. `SourceFormat::Jpg` is an input spelling
  that is sent as `jpeg`, and `SourceFormat::Url` tells the server to fetch the URL in the body. So
  neither has a `TargetFormat` counterpart, and `TargetFormat::Url` doesn't compile.
- Both enums are `#[non_exhaustive]`, so a `match` on them needs a wildcard arm. The format list can
  grow in a minor version.
- To choose a format at run time, use `SourceFormat::from_name("zpl")` and
  `TargetFormat::from_name("pdf")`, which return `None` for an unknown name.
- `ConvertRequest::base64_text(true)` sends a binary document as base64 `text/plain`. Only the
  binary sources support it.

The printer languages are targets as well as sources, so `Pdf` to `Epl` and `Zpl` to `Tspl` are
real conversions. Their output is `text/plain`, but every printer language can inline raw binary,
such as EPL's `GW` and TSPL's `BITMAP`. Read `result.bytes` rather than `result.text()` whenever a
label might carry graphics. CPCL writes graphics as hex, so its output is plain text.

`result.text()` decodes with the response's charset, which isn't always UTF-8. `result.save(path)`
writes the bytes.

## Options by stage

Every option is a method on `ConversionOptions` that sets a public `Option` field. A field you leave
`None` isn't sent.

Source options change how the document is read:

| Method | Applies to | What it does |
| --- | --- | --- |
| `source_dpi(n)` | Printer-language and image sources | Sets the resolution the source was made for: how its dots or pixels map to inches. Server default 203. A PDF, design-file, XML, or JSON source returns `400`. |
| `label_size(width, height)` | Every source | Sets the media size in inches, not dots. Leave it unset to have the server detect the size. To set one side only, set the `label` field to a `LabelSize` with the other side `None`. |
| `label_index(n)` | Every source | Converts only this label, on every plan. 0-based. For an image or PDF it counts the labels after `split`, page by page. |
| `with_data(records)` | Every source | Sets the variable-field values, one record per output label. |
| `pdf_page(n)` | PDF | Converts one page. 0-based. Leave it unset to convert every page. |
| `pdf_conversion_mode(mode)` | PDF | `PdfConversionMode::Image`, the default, rasterizes the page. `Native` extracts its text and vectors. |
| `zpl_commands_to_ignore(commands)` | ZPL and ESC/Label | Skips the named commands, such as `^PQ`. |
| `split(rows, columns)` | Image and PDF | Cuts each page into a grid of labels, read left to right, then down. Rows and columns are 1 to 20. |
| `split_label_size(width, height)` | Image and PDF | Sets one label's size on the page, in inches. Leave it unset to divide the page equally. |
| `split_margins(left, top)` | Image and PDF | Sets the distance from the page's left and top edges to the first label, in inches. |
| `split_gaps(column, row)` | Image and PDF | Sets the space between columns and between rows, in inches. |
| `split_skip_blank(true)` | Image and PDF | Leaves out labels with nothing printable. |
| `scaling(percent)` | Image and PDF | Sets the size the picture is rendered at. Server default 100. |
| `position(x, y)` | Image and PDF | Places the picture on the label, in dots. A negative offset crops it. |
| `color_mode(mode)` | Image and PDF | Sets how the picture is reduced: `ColorMode::Bw`, `Grayscale` (the default), or `Color`. |
| `darkness(n)` | Image and PDF | Sets the luminance threshold, 0 to 100. Server default 70. |

Target options change what's written:

| Method | Applies to | What it does |
| --- | --- | --- |
| `target_dpi(n)` | Every target but PDF | Sets the resolution of the output. Server default: `source_dpi`, else 203. A PDF target returns `400`. |
| `rotation(degrees)` | Every target | Turns the finished label clockwise. Must be a multiple of 90. |
| `watermark(bool)` | Every target | Turns the watermark on or off. An explicit `false` is sent. The free tier forces it on. |
| `dialect(name)` | Every target | Sets a printer dialect, such as `"moca"`. Requires a paid license. |
| `color_mode(mode)`, `darkness(n)` | Image targets | `ColorMode::Bw` renders a black-and-white image at the `darkness` threshold. |
| `zpl_image_compression(mode)` | ZPL | Sets how embedded images are encoded: `ZplImageCompression::Z64`, the default, or `CompressedHex`. |
| `zpl_collapse_copies(true)` | ZPL from an image or PDF | Sends each run of identical labels as one label with a `^PQ` count. |
| `layout(across, gap)` | A printer language from an image or PDF | Puts `across` labels (1 to 10) side by side in each printed row, `gap` inches apart. |
| `layout_fill(fill)` | A printer language from an image or PDF | `LayoutFill::Sequential`, the default, puts each page in the next column. `Repeat` fills the whole row with each page. |

`color_mode` and `darkness` are one setting each. On an image or PDF source they shape the picture
that's read, and on an image target they shape the output.

`zpl_collapse_copies`, `layout`, and `split` need a PDF in `Image` mode, the default. With `Native`,
more than 1 across or a split grid returns `400`, and `zpl_collapse_copies` has no effect.

To send a parameter the crate doesn't model, use `extra(key, value)`. The server ignores keys it
doesn't know.

## Errors

Every call returns `Result<_, labelzoom::Error>`, an enum with three variants:

| Variant | Meaning |
| --- | --- |
| `Error::Api(ApiError)` | The API returned a non-2xx response. |
| `Error::Validation(ValidationError)` | The SDK rejected the request before sending it, such as a rotation that isn't a multiple of 90. It has `parameter` and `message`, and is never retried. |
| `Error::Transport(TransportError)` | No usable response arrived: a connection failure, a timeout, or a print response that wasn't JSON. |

`ApiError` has these fields:

| Field | Value |
| --- | --- |
| `kind` | The `ApiErrorKind` for the status. |
| `status` | The HTTP status code. |
| `message` | The detail from the body, up to 512 characters. |
| `code` | The body's `code` string, such as `agent_update_required`, or `None`. Branch on `code`, not on `message`. |
| `request_id` | The `X-LZ-Request-Id` response header. Quote it to LabelZoom support. |
| `raw_body` | The whole response body. |

| `ApiErrorKind` | Status |
| --- | --- |
| `BadRequest` | `400` |
| `Unauthorized` | `401` |
| `Forbidden { is_paid_feature }` | `403`. `is_paid_feature` is `true` when the free tier asked for a paid feature. |
| `NotFound` | `404` |
| `PayloadTooLarge` | `413` |
| `Unprocessable` | `422`. Print returns it with `code` `agent_update_required` when the printer's agent is too old to print copies, and `idempotency_key_reused` when the key was used for a different request. |
| `RateLimited { retry_after_seconds }` | `429` |
| `PrintQuotaExceeded { retry_after_seconds }` | `429` with `code` `daily_job_limit_reached`. A kind of rate limit: `e.is_rate_limited()` is `true` for both. |
| `ServerError` | `5xx` |
| `Other` | Any other status, such as `406`. |

```rust
use labelzoom::{ConvertRequest, Error, LabelZoomClient, SourceFormat, TargetFormat};

let client = LabelZoomClient::new();
let request = ConvertRequest::new(SourceFormat::Zpl, TargetFormat::Json, "^XA^FO50,50^FDHello^FS^XZ");

match client.convert(&request) {
    Ok(result) => result.save("label.json")?,
    Err(Error::Api(e)) if e.is_paid_feature() => eprintln!("needs a paid plan: {}", e.message),
    Err(Error::Api(e)) if e.is_rate_limited() => {
        eprintln!("rate limited; retry after {:?} s", e.retry_after_seconds());
    }
    Err(Error::Api(e)) => eprintln!(
        "HTTP {} ({:?}), request ID {:?}: {}",
        e.status, e.code, e.request_id, e.message
    ),
    Err(Error::Validation(e)) => eprintln!("bad {}: {}", e.parameter, e.message),
    Err(e) => eprintln!("no response: {e}"),
}
```

`Error`, `ApiError`, and `ApiErrorKind` are `#[non_exhaustive]`, so a `match` on `Error` or
`ApiErrorKind` needs a wildcard arm, and you read `ApiError`'s fields rather than build one.
`Error::as_api()` returns the `ApiError` inside, if there is one.

## Retries

The client retries `429` responses, `5xx` responses, and transport failures: twice by default, for
three attempts in all, with a 1 s, 2 s, then 4 s backoff under full jitter. It honors a
`Retry-After` header on any retryable status when it asks for a longer wait than the backoff. It
never retries any other `4xx`, a validation error, or `PrintQuotaExceeded`.

| Builder method | What it does |
| --- | --- |
| `max_retries(n)` | Sets the retries after the first attempt. `0` turns retrying off. |
| `jitter(false)` | Makes the backoff exactly 1 s, 2 s, then 4 s. Leave jitter on in production. |
| `sleeper(sleeper)` | Replaces the wait between attempts with your own `Sleeper`. |

```rust
let client = LabelZoomClient::builder().max_retries(0).build()?; // no retries
```

## Use the client from async code

The client blocks the calling thread. In async code, call it inside your runtime's blocking pool,
such as `tokio::task::spawn_blocking`.

## Test your own code

The HTTP backend, the wait between retries, and the environment lookup are all injectable, so a
test opens no socket, doesn't sleep, and doesn't read a real API key:

```rust
use labelzoom::{HttpRequest, HttpResponse, LabelZoomClient, Sleeper, Transport, TransportError};
use std::sync::Arc;

struct Stub;

impl Transport for Stub {
    fn execute(&self, _request: HttpRequest) -> Result<HttpResponse, TransportError> {
        Ok(HttpResponse {
            status: 200,
            headers: vec![("content-type".into(), "text/plain".into())],
            body: b"^XA^XZ".to_vec(),
        })
    }
}

struct NoSleep;

impl Sleeper for NoSleep {
    fn sleep(&self, _seconds: f64) {}
}

let client = LabelZoomClient::builder()
    .transport(Arc::new(Stub))
    .sleeper(Arc::new(NoSleep))
    .jitter(false)
    .env_lookup(|_| None)
    .build()?;
```

`HttpRequest` carries the method, URL, headers, and body the client would send, so a stub can
assert on them. `client.request_url(&request)` returns the URL a conversion would be sent to,
without sending it.

## Migrate from 1.x

| 1.x | 2.x |
| --- | --- |
| `.dpi(n)` with a printer-language or image source | `.source_dpi(n)`. The output is written at the same resolution unless you also set `target_dpi`. |
| `.dpi(n)` with a PDF, design-file, XML, or JSON source | `.target_dpi(n)` |
| `.dpi(n)` with a PDF target | Remove it. A PDF target takes no resolution, and returns `400` for `target_dpi`. |
| `ConversionOptions { dpi: Some(n), .. }` | The `source_dpi` or `target_dpi` field, as in the rows above. |
| Building `ApiError` with a struct literal, or destructuring it without `..` | Read its fields. `ApiError` is `#[non_exhaustive]` and has a new `code` field. |
| A `match` arm for `ApiErrorKind::RateLimited` | `e.is_rate_limited()`, which also covers the new `PrintQuotaExceeded` kind. A `422` is now `ApiErrorKind::Unprocessable` rather than `Other`. |
| No print API | `client.print(&PrintRequest::new(source, body, printer_id))`, `client.print_template(&TemplatePrintRequest::new(template_id, printer_id))`, `client.get_print_job(job_id)`, and `client.get_print_quota()`. |

To move a label to a printer of another density at the same physical size, set both:
`.source_dpi(300).target_dpi(203)`. Every other 1.x call keeps its name and meaning.

## Contributing

From this directory:

```sh
cargo fmt --check
cargo clippy --all-targets --all-features -- -D warnings
cargo test
cargo build --no-default-features     # the build with no bundled HTTP stack
```

`cargo test` runs the shared conformance fixtures in [`../conformance/`](../conformance/), plus a
check that it executed every case. `conformance/skips/rust.json` declares each skipped case with
its reason. The two `typecheck/*` cases build a snippet from `tests/typecheck/snippets/` in a
throwaway crate and assert that the compiler rejects it with the expected error code. To add or
change a case, see [Writing a conformance runner](../docs/CONFORMANCE.md).

The test suite makes no network calls. `cargo run --example smoke` runs one anonymous conversion
against the live API.

## License

MIT. See [LICENSE](LICENSE).
