![LabelZoom Logo](../docs/LabelZoom_Logo_f_400px.png)

# LabelZoom Rust SDK

Official Rust client for the [LabelZoom API](https://api.labelzoom.com). Converts barcode labels
between ZPL, EPL, IPL, TSPL, DPL, SBPL, PDF, LabelZoom XML/JSON, and raster images.

Rust 1.85+. Blocking, with a rustls-backed HTTP stack — no async runtime, and no OpenSSL headers
needed to build it anywhere.

## Install

```sh
cargo add labelzoom
```

```toml
labelzoom = "1.0"
```

<details>
<summary>Bring your own HTTP stack</summary>

```toml
labelzoom = { version = "1.0", default-features = false }
```

Drops `ureq`, `rustls` and `ring` entirely and leaves you to supply a
[`Transport`](#testing-your-own-code). CI builds this configuration on every run, so it stays a
real, working mode rather than an aspiration.
</details>

## Quick start

**An API key is optional.** Without one you get the free tier — watermarked output, first label
only, a 1 MB request cap, and no multi-page, JSON-target, or image-to-image conversion.

```rust
use labelzoom::{ConversionOptions, ConvertRequest, LabelZoomClient, SourceFormat, TargetFormat};

let client = LabelZoomClient::new();          // anonymous; this works

let request = ConvertRequest::new(
    SourceFormat::Zpl,
    TargetFormat::Png,
    "^XA^FO20,20^A0N,28^FDHello^FS^XZ",
)
.options(ConversionOptions::new().dpi(300).label_size(4.0, 6.0));

client.convert(&request)?.save("label.png")?;
```

With a credential:

```rust
let client = LabelZoomClient::builder().api_key("lz_live_...").build()?;
```

`LabelZoomClient::new()` reads `LABELZOOM_API_KEY` from the environment. `.api_key("")` — or the
more readable `.anonymous()` — forces the free tier and suppresses that fallback.

### Why a value struct and not a fluent chain

The .NET, Node, Java and PHP SDKs expose `client.convert().fromZpl(body).toPng().execute()`. A
chain that defers its errors to a terminal `send()` is as un-Rust as it is un-Go, and `Option<T>`
already expresses "not set" without one. The wire behaviour is identical — that is what the shared
conformance suite proves. See [API_CONTRACT.md §9](../docs/API_CONTRACT.md#9-divergences).

## Formats

**Sources (15):** `Zpl` `Epl` `Ipl` `Tspl` `Dpl` `Sbpl` `Xml` `Json` `Pdf` `Png` `Bmp` `Gif` `Jpeg`
`Jpg` `Url`

**Targets (13):** `Zpl` `Epl` `Ipl` `Tspl` `Dpl` `Sbpl` `Xml` `Json` `Pdf` `Png` `Bmp` `Gif` `Jpeg`

`SourceFormat` and `TargetFormat` are separate enums. `SourceFormat::Jpg` is an input spelling
that normalizes to `jpeg` on the wire, and `SourceFormat::Url` tells the server to go fetch a
document rather than naming a format — so neither has a `TargetFormat` counterpart, and
`TargetFormat::Url` does not compile.

Both enums are `#[non_exhaustive]`, and that is not boilerplate: `Epl`, `Tspl` and `Dpl` became
*targets* in contract 1.1.0, when the printer-language writers shipped. Without it, every such
addition would be a breaking change for any downstream `match`.

The printer languages round-trip: `pdf`→`epl` and `zpl`→`tspl` are real conversions. Their output
is `text/plain`, but EPL's `GW` and TSPL's `BITMAP` commands inline raw binary, so read
`result.bytes` rather than `result.text()` whenever a label might carry graphics.

## Options

Every field of `ConversionOptions` is an `Option`, and **only the ones you set are sent** —
`#[serde(skip_serializing_if = "Option::is_none")]` is that rule expressed in the type system. The
SDK never substitutes a default of its own, so a change to a server default reaches you without a
crate upgrade.

```rust
ConversionOptions::new()
    .dpi(300)                                   // server default 203
    .rotation(90)                               // must be a multiple of 90
    .scaling(75.0)                              // percent; server default 100
    .color_mode(ColorMode::Grayscale)
    .darkness(60)                               // 0-100 luminance threshold
    .watermark(false)                           // an explicit false IS sent
    .label_size(4.0, 6.0)                       // INCHES
    .pdf_page(0)                                // 0-BASED
    .zpl_commands_to_ignore(["^PQ"])
```

Two units are routinely misread and are pinned by the shared fixtures:

- `label_size` is in **inches**, not dots. Leave it unset to have the server detect the size.
- `pdf_page` is **0-based**. Leave it unset to convert every page.

`data` is one record per output label; `ConversionOptions::extra` carries anything the crate does
not model yet, since unknown keys are ignored server-side.

## Errors

Rust has no inheritance, so rule E4's "every API error shares one base type" is a single enum
variant:

```rust
match client.convert(&request) {
    Ok(result) => { /* … */ }
    Err(Error::Api(e)) if e.is_paid_feature() => { /* the anonymous tier hit a paywall */ }
    Err(Error::Api(e)) => eprintln!("request {:?} failed with {}: {}", e.request_id, e.status, e.message),
    Err(Error::Validation(e)) => eprintln!("bad {}: {}", e.parameter, e.message),
    Err(Error::Transport(e)) => eprintln!("no response: {e}"),
}
```

`ApiError` carries the status, the message, the untruncated raw body, and the `X-LZ-Request-Id`
support handle; `ApiErrorKind` says which class it is, with the per-status data on the variant
that has it (`Forbidden { is_paid_feature }`, `RateLimited { retry_after_seconds }`).

`Error::Validation` is a **sibling** of `Error::Api`, not a member: it reports a request rejected
locally, before any network call, which is a bug in the calling code rather than a server response.

## Retries

429s, 5xx responses and transport failures are retried automatically — twice by default, for three
attempts — with a 1s/2s/4s backoff under full jitter. A `Retry-After` header is honoured on any
retryable status when it asks for longer than the backoff would wait. No other 4xx is ever retried.

```rust
let client = LabelZoomClient::builder().max_retries(0).build()?;   // disable retrying
```

## Testing your own code

The HTTP backend, the sleeper and the environment lookup are all injectable, so a test never opens
a socket, never sleeps, and never picks up a developer's real key:

```rust
use labelzoom::{HttpRequest, HttpResponse, Transport, TransportError};

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

let client = LabelZoomClient::builder()
    .transport(Arc::new(Stub))
    .sleeper(Arc::new(RecordingSleeper::default()))
    .jitter(false)
    .env_lookup(|_| None)
    .build()?;
```

`Transport` is part of the crate's design rather than a test-only afterthought — it is also how
`default-features = false` works.

## Development

```sh
cargo fmt --check
cargo clippy --all-targets --all-features -- -D warnings
cargo test
cargo build --no-default-features     # proves the Transport seam is real
```

The test suite is the shared conformance fixtures in [`../conformance/`](../conformance/) — the
same 87 cases the .NET, Node, Java, Python, PHP, Go and Ruby suites run — plus an assertion that it
executed every one of them. `conformance/skips/rust.json` is empty: Rust compiles, so the two
`typecheck/*` cases are run for real, by building a snippet from `tests/typecheck/snippets/` in a
throwaway crate and asserting the compiler rejects it with the expected error code.

`cargo run --example smoke` does one anonymous conversion against the live API. It is not part of
the test suite, which is offline by design.

## License

MIT — see [LICENSE](LICENSE).
