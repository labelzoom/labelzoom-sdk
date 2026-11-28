# LabelZoom SDK — API Contract

**Status:** normative. **Applies to:** every language SDK in this repository.

This document states, once, the behavior every LabelZoom SDK must exhibit. It exists because
the SDKs are written independently in eight languages and would otherwise drift. Each rule has
a stable identifier (`A1`, `E4`, …) so fixtures, code comments, and review feedback can cite it.

The machine-checkable subset of these rules lives in [`../conformance/`](../conformance/) and is
executed by every language's test suite. See [CONFORMANCE.md](CONFORMANCE.md).

> **The wire is identical everywhere; the ergonomics are not.** Nothing in this document
> constrains method naming, chaining style, or async model. See [§9 Divergences](#9-divergences).

---

## 1. The canonical shape

```
client = LabelZoomClient(apiKey?, baseUrl?, timeout?, maxRetries?, userAgent?)

result = client.convert()
             .from(<SourceFormat>, <body>)                      ── source stage
                 .withDpi(int)                         (sourceDpi; printer languages and images)
                 .withLabelSize(widthInches, heightInches)
                 .withLabelIndex(zeroBasedIndex)
                 .withData([{...}, ...])
                 .withPdfConversionMode(IMAGE|NATIVE) .withPdfPage(zeroBasedIndex)
                 .withZplCommandsToIgnore([...])
                 .withSplit(rows, columns, width, height, left, top,
                            columnGap, rowGap, skipBlank)      (a group: see 1.2)
                 .withRaster(scaling, position(x, y), darkness,
                             colorMode BW|GRAYSCALE|COLOR)      (a group: see 1.2)
             .to(<TargetFormat>)                                ── target stage
                 .withDpi(int)                         (targetDpi; every target but PDF)
                 .withRotation(int)
                 .withWatermark(bool) .withDialect(string)
                 .withColorMode(BW|GRAYSCALE|COLOR) .withDarkness(int)   (image targets)
                 .withZplImageCompression(Z64|COMPRESSED_HEX)
                 .withZplCollapseCopies(bool)
                 .withLayout(across, gapInches, fill)           (a group: see 1.2)
             .execute()
      -> ConversionResult { bytes, text, contentType, status, requestId }

job = client.convert()
             .from(<SourceFormat>, <body>)                      ── source stage, unchanged
                 (the source options above)
             .toPrinter(printerId)                              ── printer target stage
                 .withRotation(int)
                 .withCopies(1..9999) .withIdempotencyKey(string)
             .execute()
      -> PrintJob { jobId, status, copies, idempotent }

batch = client.convert()
             .fromTemplate(templateId)                          ── template source stage
                 .withData([{...}, ...]) .withValidate(bool)
             .toPrinter(printerId)                              ── template printer stage
                 .withCopies(1..9999) .withIdempotencyKey(string)
             .execute()
      -> PrintBatch { jobs: [PrintJob], remaining, message }

details = client.jobs.get(jobId)      -> PrintJobDetails
quota   = client.getPrintQuota()      -> PrintQuota
```

**A printer is a target.** Printing runs the same pipeline in the same order: the source, how it
is read, then where it goes. `toPrinter(printerId)` stands where `toZpl()` would, and the server converts the
document to the printer's configured language. The printer target stage offers `withRotation`,
because rotation turns the finished label for every format; it offers no other target option,
because the rest depend on an output language the request doesn't name. A published template is a
source of its own: it takes merge data instead of source options, and for now its only target is a
printer. See **P** in §2.

The chain has two stages. **Source options**, which change how the source document is read, are
set after `from` and before `to`. **Target options**, which change the output, are set after `to`.
`label.*` is a source option because the caller's size goes *into* the reader (see the size
precedence in §4.1); `split` is a source option because it cuts the input page.

`raster` is a source option for the same reason as `split`: `scaling`, `position`, `darkness` and
`colorMode` act on the picture an image or PDF source is rasterized to. `scaling` sets the size the
picture is rendered or resampled at; `position` places it on the label, and a negative offset crops
it, so `position` with `label.*` is how a picture is cropped; `darkness` and `colorMode` reduce it to
black and white. The server ignores all four for any other source. `rotation` is the exception: it
turns the finished label for every format pair, so it is a target option.

`colorMode` and `darkness` also have an output meaning, for image targets only: `BW` renders a
black-and-white image at the `darkness` threshold. They are one wire key each, so the image target
stage offers them again rather than leaving a ZPL-to-PNG caller no way to ask for black and white.
A request that sets them on both stages sends the last value set. The stages are
ergonomics only: both write into the one `?params=` object (**C1**), so a request built through
the stages is byte-for-byte the request 1.x built through one flat builder.

### 1.1 Two format types

```
SourceFormat  (24)  ZPL EPL IPL TSPL DPL SBPL ESCLABEL FINGERPRINT EZPL CPCL TPCL
                    NLBL LBL BTW DYMO XML JSON PDF PNG BMP GIF JPEG JPG URL
TargetFormat  (14)  ZPL EPL IPL TSPL DPL SBPL CPCL XML JSON PDF PNG BMP GIF JPEG
```

`SourceFormat` and `TargetFormat` are **distinct types**, and the sets are not the same. `JPG` and
`URL` are source-only: `JPG` is an input spelling that normalizes to `JPEG` on the wire (**A2**),
and `URL` is a fetch instruction rather than a format — there is no `TargetFormat.URL` to name.
The formats the API reads but doesn't write are source-only too: ESC/Label, Fingerprint, EZPL,
TPCL, NiceLabel (`NLBL`, `LBL`), BarTender (`BTW`) and DYMO.
Enforce that at compile time wherever the language allows (C#, TypeScript, Java, PHP 8.1, Go,
Rust);
`Literal[...]` + mypy in Python and a runtime `ArgumentError` in Ruby are acceptable substitutes.
Do not contort a language to get compile-time enforcement it does not natively offer.

> `EPL`, `TSPL` and `DPL` became targets when the printer-language writers shipped
> (on 2026-08-23). They were
> source-only for the whole of `0.x`, and the SDKs asserted that in four `typecheck/*` cases. Those
> assertions are gone; do not reintroduce them.
>
> `IPL` (Intermec) and `SBPL` (SATO) arrived later still, as sources *and* targets from the start —
> IPL on 2026-09-02, SBPL on 2026-09-04. Contract 2.1.0 added nine more sources and the `CPCL`
> target, all of which the API already accepted. This set has now grown three times. Treat it as
> open: model the
> formats so that adding one is additive, which is what `#[non_exhaustive]` buys in Rust and what a
> derived token map buys everywhere else.

### 1.2 Stage builders

Each stage has **one base builder** that owns everything format-independent: the shared options,
the `to*` transitions (source stage) and `execute` (target stage). Named convenience methods
return a subclass that adds only the options for that format:

| Method | Returns | Adds |
|---|---|---|
| `fromPdf` | PDF source stage | `withPage`, `withConversionMode`, `withSplit`, `withRaster` |
| `fromPng`, `fromBmp`, `fromGif`, `fromJpeg` | image source stage | `withDpi`, `withSplit`, `withRaster` |
| `fromZpl`, `fromEscLabel` | ZPL source stage | `withDpi`, `withCommandsToIgnore` |
| `fromEpl`, `fromIpl`, `fromTspl`, `fromDpl`, `fromSbpl`, `fromFingerprint`, `fromEzpl`, `fromCpcl`, `fromTpcl` | printer-language source stage | `withDpi` |
| `fromXml`, `fromJson`, `fromNlbl`, `fromLbl`, `fromBtw`, `fromDymo` | plain source stage | nothing |
| `from(SourceFormat, body)`, `fromUrl`, base64 text | dynamic source stage | every source option, format-prefixed |
| `toZpl` | ZPL target stage | `withDpi`, `withImageCompression`, `withCollapseCopies`, `withLayout` |
| `toPng`, `toBmp`, `toGif`, `toJpeg` | image target stage | `withDpi`, `withColorMode`, `withDarkness` |
| `toPdf` | PDF target stage | nothing |
| other `to*` | plain target stage | `withDpi` |
| `to(TargetFormat)` | dynamic target stage | every target option, format-prefixed |

So `fromZpl(zpl).withPage(0)`, `fromPdf(pdf).withDpi(300)` and `toPdf().withLayout(...)` don't
compile, in the languages that chain (C#, Java, TypeScript, PHP). The dynamic builders exist
because a format chosen at run time
can't be checked at compile time; they keep the format prefix (`withPdfPage`) so the name still
says where the option acts.

**Groups.** `split`, `layout` and `raster` have several fields and are set as one group, in the language's
idiom for a group: a lambda over an options object in C# and Java (`withSplit(s -> s.rows(2)
.columns(2))`), an object literal in TypeScript (`withSplit({ rows: 2, columns: 2 })`), and the
nested structure the language already uses for options elsewhere (§9). Don't model a group as
nested chain scopes, where `.split().rows(2)` returns a scope that must also offer `to*` and every
outer method: the exit methods multiply with every level.

> **Why these subclasses aren't the PR #6 mistake.** The superseded fluent API (PR #6) used a
> class per format — `PdfSourceBuilder`, `ZplSourceBuilder`, `RasterSourceBuilder`,
> `ZplTargetBuilder`, `PdfTargetBuilder`, `RasterTargetBuilder`. Each independently knew the format
> matrix, and they drifted: four `With*` options were silently dropped, `GetSourceFormat()`
> handled only 2 of 12 sources, and `RasterTargetBuilder` emitted the *source* type as the target.
> At 15 x 13 that architecture needed ~28 classes and 195 paths nobody could keep consistent.
>
> The stage subclasses keep the property that design lacked: **no subclass knows a format**. These
> rules hold it, and a change that breaks one reintroduces the old failure:
>
> - The format is fixed once, by the `from*`/`to*` call, and travels with the request. A subclass
>   never maps a format to a token, a media type or a URL segment.
> - Every option is written by **one** shared function per option. A subclass method is a
>   one-line call into it, so `withPage` and `withPdfPage` cannot disagree on validation or on
>   the JSON path.
> - Subclasses exist only where a format has options of its own. A new source or target format
>   with no options of its own gets the plain stage, not a new class.
> - The conformance suite runs through the dynamic builders, so every shared writer is exercised
>   by every fixture. Each SDK also has a test that sends one request through the typed
>   builders and checks its `params`.
>
> **Print adds targets, not source stages.** `toPrinter` is one more transition on the base source
> stage, so every source stage, and every source option, reaches a printer with no print-specific
> source classes. The template source stage is the one new source class, because a template takes
> merge data rather than a document. Both printer stages write `rotation` through the same shared
> writer the convert target stage uses.

### 1.3 Format metadata table

Defined **once** per language. Never inlined at a call site.

| SourceFormat | request `Content-Type` | base64/text alternative |
|---|---|---|
| `ZPL` `EPL` `IPL` `TSPL` `DPL` `SBPL` | `text/plain` | — |
| `ESCLABEL` `FINGERPRINT` `EZPL` `CPCL` `TPCL` | `text/plain` | — |
| `NLBL` `LBL` `BTW` | `application/octet-stream` | `text/plain` |
| `DYMO` | `application/xml` | — |
| `XML` | `application/xml` | — |
| `JSON` | `application/json` | — |
| `PDF` | `application/pdf` | `text/plain` |
| `PNG` | `image/png` | `text/plain` |
| `BMP` | `image/bmp` | `text/plain` |
| `GIF` | `image/gif` | `text/plain` |
| `JPEG` / `JPG` | `image/jpeg` | `text/plain` |
| `URL` | `text/plain` (body is the URL) | — |

ESC/Label is a dialect of ZPL, so it gets the ZPL source stage. NiceLabel, BarTender and DYMO files
measure in physical units rather than dots, so the API rejects `sourceDpi` for them with a 400, and
they get the plain source stage. NiceLabel and BarTender files are binary: send the bytes, never a
decoded string.

---

## 2. Rules

### A — Routing

- **A1** `POST {baseUrl}/api/v2/convert/{sourceFormat}/to/{targetFormat}`, format tokens lowercased.
- **A2** Source `jpg` normalizes to `jpeg` before path construction. `SourceFormat` carries both
  spellings because callers describe files they already have; `TargetFormat` carries only `Jpeg`,
  so a target needs no normalization — there is no other spelling to write.
- **A3** Default `baseUrl` is `https://api.labelzoom.com`. Trailing slashes are normalized away.
  `api.labelzoom.net` and `www.labelzoom.net` are legacy hosts and **must not** appear in shipped
  code, samples, or documentation.
- **A4** `SourceFormat.URL` posts the URL string as the request body with `Content-Type: text/plain`.
  The server then fetches that URL itself. Document the trust boundary in the method's docstring —
  this hands an arbitrary caller-supplied URL to a server-side fetch.

### B — Headers

- **B1** `Content-Type` is the exact media type from [§1.3](#13-format-metadata-table). When the
  caller opts into base64/text mode, send `text/plain`.

  > This is SDK style, not a server limit. The source format comes from the URL path, and the
  > server rejects a `Content-Type` only when it names a *different* format than the path does —
  > `application/pdf` posted to `/convert/zpl/to/png` is a **400**. Everything that declares
  > nothing is accepted for every source format: `application/octet-stream`, any `text/*`
  > subtype, and no `Content-Type` header at all.
  >
  > SDKs still send the exact type, because it is the one choice that is right for every payload
  > and it keeps §1.3 as the single place a format's metadata lives. But a caller that holds raw
  > bytes and no reliable media type — a file picker, a queue payload — can send
  > `application/octet-stream` rather than inventing one. Note that for `PDF` and the image
  > formats, `text/plain` still means base64 (the third column of §1.3), so raw binary must go as
  > `application/octet-stream` or the exact type, never as `text/plain`.
  >
  > A body sent as `application/octet-stream`, or with no `Content-Type`, is decoded as
  > ISO-8859-1 — every byte round-trips, which is what a printer language with an inline binary
  > graphic needs. A declared `text/*` or `application/xml` without a `charset` parameter is
  > decoded as UTF-8. Send an explicit `charset` whenever the payload is text in anything else.
- **B2** **`Accept: */*`, always.** One value for every target, chosen so no SDK carries a
  per-target header table.

  > This used to be a workaround. The server's `produces` list omitted `image/gif`, `image/bmp`
  > and `image/jpeg`, so an exact `Accept` drew a **406** from content negotiation before the
  > handler ran — a server defect that this contract had written down as if it were API design.
  > It was fixed on 2026-08-29: `produces` now lists every target it can emit, and the
  > independent service-level check compares with `isCompatibleWith`, so an exact media type, a
  > wildcard subtype (`image/*`) and a `charset`/`q` parameter are all accepted.
  >
  > `*/*` therefore stays for a plain reason rather than a defensive one: it is the one value
  > valid for all fourteen targets, so the request builder needs no branch. A `strictAccept`
  > escape hatch may be offered, but it is **off by default**.

- **B3** `Authorization: Bearer <credential>` is sent **only** when a credential is present. When
  absent, the header must be **absent** — never `Bearer `, never `Bearer null`.
- **B4** `User-Agent: labelzoom-<lang>-sdk/<version> (<runtime>)`. It must **not** begin with
  `LabelZoomStudio/` — the server parses that prefix as a Studio version and silently forces
  `pdf.conversionMode = NATIVE` for versions <= 1.8.2.

### C — Parameter serialization

- **C1** Only options the caller **explicitly set** are serialized. Never emit a client-side
  default. `targetDpi=203` appearing in a request the caller never configured is a bug: it defeats any
  future server-side default change. Implementations need a way to distinguish "unset" from
  "set to the zero value" (pointer fields in Go, a sentinel in Python, nullables elsewhere).
- **C2** All options travel in exactly **one** `?params=<url-encoded JSON>` query parameter. Do
  **not** emit dot-notation (`?targetDpi=300&label.width=4`).

  > This is now a **style rule, not a capability limit**. It used to be both: `?data=[{},{}]`
  > returned **400** because the server had no String conversion registered for the `data` field's
  > type, making it the one parameter dot-notation could not express. That was fixed on
  > 2026-08-29; both spellings now work, and where they overlap the dot-notation value wins.
  >
  > Keep emitting one `?params=` object anyway. One serialization path with no per-field special
  > cases is what keeps a fifteen-option builder consistent across eight languages, and it is the
  > shape the conformance fixtures pin. `withRawQueryParam(key, value)` is the escape hatch for
  > anything not yet modeled.

- **C3** `data` is always a JSON **array** of objects, one entry per output label. A single object
  passed by the caller is wrapped into a one-element array. A non-object array element is a local
  validation error.
- **C4** `label.width` / `label.height` are **inches** (float). `pdf.pageNumber` is **0-based** and
  is omitted entirely to convert all pages. Both facts must appear in the docstring of every
  language's corresponding method — they are the two most misread parameters in the API.
  Leaving the label size unset must emit **no `label` key at all**: that absence is what asks the
  API to detect the label's dimensions from its content, so an SDK that helpfully defaults it to
  4×6 silently overrides detection for every caller who never asked for a size.

  The same two facts extend to the multi-label parameters. `layout.gap` and every `split.*`
  length are **inches**, because they describe the media and the source sheet. The media, and a
  PDF page, do not change size when `targetDpi` or `scaling` does, so neither does a gap or a
  PDF's grid. An image has no physical size: the server measures its page as pixels ÷ `sourceDpi`,
  so on an image source the same `split` inches cover different pixels at a different `sourceDpi`.
  (`position`
  is the one length still in dots, and it predates this rule.) `labelIndex` is **0-based**, and
  `0` must serialize. Leaving `split.width` / `split.height` unset must emit neither key: that
  absence is what asks the API to divide the page equally.
- **C5** Enums serialize to exact uppercase wire tokens: `BW|GRAYSCALE|COLOR`, `IMAGE|NATIVE`,
  `Z64|COMPRESSED_HEX`, `SEQUENTIAL|REPEAT`.
- **C6** `rotation` must be a multiple of 90, validated **locally**. This raises a validation error
  type that is **distinct from every API error type** and is never retried.
- **C7** When no options are set, emit no query string at all — a bare URL.
- **C8** A resolution travels as `sourceDpi` from the source stage and `targetDpi` from the
  target stage. **Never emit the legacy `dpi` key.** The server still accepts it, and fills both
  sides from it wherever a conversion uses one, but a 2.0 SDK has no way to set it, because one
  number for two resolutions is the ambiguity the split exists to remove.

  `sourceDpi` is the resolution a printer-language job or an image was made for: how its dots or
  pixels map to inches. It also beats the resolution an ESC/Label or CPCL job declares, as a
  caller's label size beats the job's own. `targetDpi` is the resolution of the output. The server
  follows a `sourceDpi` sent alone, so naming the job's resolution doesn't also rescale it; send
  both to move a label to a printer of another density at the same physical size.

  The server answers **400** for `sourceDpi` from a source with no dots (PDF, NiceLabel,
  BarTender, LabelZoom XML and JSON), for `targetDpi` with the PDF target, and for a value that
  isn't positive. The SDKs check only the last one locally; which formats take which is server
  policy, and the typed stages already leave the method off where it can't apply.

### D — Response

- **D1** Success returns `ConversionResult { bytes, text, contentType, status, requestId }`.
  `bytes` is authoritative; `text` decodes using the response charset, defaulting to UTF-8.

  > Returning a bare string is wrong: `PDF`, `PNG`, `BMP`, `GIF`, and `JPEG` targets are binary.

- **D2** `requestId` is read **case-insensitively** from `X-LZ-Request-Id`. The gateway sets
  `X-LZ-Request-Id` but CORS-exposes it as `X-LZ-Request-ID`. Absent header yields null/empty;
  reading it never throws. Surface it on success **and** on every error — it is the support handle.

### E — Errors

- **E1** Every non-2xx response becomes a typed error. The response body is **never** discarded;
  it carries the server's error detail.
- **E2** Message extraction, in order:
  1. JSON `.message` (top-level)
  2. raw body text, truncated to 512 characters
  3. the HTTP reason phrase

  > The API's error body is an RFC 9457 problem document, sent as `application/problem+json`:
  >
  > ```json
  > { "type": "about:blank", "title": "Bad Request", "status": 400,
  >   "detail": "Query parameter 'params' is not valid JSON",
  >   "instance": "/api/v2/convert/zpl/to/pdf",
  >   "message": "Query parameter 'params' is not valid JSON",
  >   "requestId": "..." }
  > ```
  >
  > `message` is a permanent extension member equal to `detail`, so step 1 covers it, and it is
  > written before any other extension member. Do not branch on the `application/problem+json`
  > media type: parse any JSON error body the same way.
  >
  > Older servers send `{"message": "..."}` or `{"timestamp","status","error","message","path"}`,
  > and step 1 covers both. Keep handling them, because a deployment can lag behind the newest
  > shape. The `response/error-*` fixtures pin every shape.
- **E3** Errors carry `status`, `message`, `requestId`, `rawBody`, and `code`. A 429 additionally
  carries `retryAfterSeconds`, parsed from `Retry-After`. `code` is the error body's top-level
  `code` string, such as `agent_update_required`, and null when the body has none. It is stable,
  so callers branch on it rather than on `message`.
- **E4** Status maps to type:

  | Status | Type |
  |---|---|
  | 400 | `BadRequest` |
  | 401 | `Unauthorized` |
  | 403 | `Forbidden` |
  | 404 | `NotFound` |
  | 413 | `PayloadTooLarge` |
  | 422 | `Unprocessable` |
  | 429 | `RateLimited` |
  | 429 with `code` `daily_job_limit_reached` | `PrintQuotaExceeded`, a kind of `RateLimited` |
  | 5xx | `ServerError` |

  All share a single base type so a caller can catch everything with one handler. Any other
  status, such as 409, is the base type. `PrintQuotaExceeded` is a subtype of `RateLimited` where
  the language has subtypes, so a handler for `RateLimited` still catches it.
- **E5** A 403 whose message matches `/paid feature/i` sets an `isPaidFeature` flag. This is the
  most common anonymous-tier failure (`JSON export is a paid feature`, `MOCA export is a paid
  feature`, `Conversion between image and document formats is a paid feature`) and deserves a
  first-class signal rather than string-matching by the caller.

### F — Retry

- **F1** Retry only on 429, 5xx, and transport-level connect/timeout errors. **Never** on any other
  4xx.
- **F2** Default 3 attempts total (2 retries). Backoff 1s / 2s / 4s with full jitter. When
  `Retry-After` is present and larger than the computed backoff, honor `Retry-After`.
- **F3** `maxRetries = 0` disables retry entirely.
- **F4** The sleep function is an **injectable internal seam**. This is a hard design requirement,
  not a suggestion: without it, retry tests either take wall-clock seconds or go untested.
- **F5** A `PrintQuotaExceeded` is **never** retried, whatever `Retry-After` says. The daily job
  limit resets at 00:00 UTC, so its `Retry-After` is hours away, and honoring it under **F2** would
  hang the call. Every other print 429 (`print_rate_limited`) is retried under F1 and F2.

### G — Anonymous mode

- **G1** Constructing a client with no credential **must succeed**. The API has a working anonymous
  free tier (watermarked, first label only per request, no multi-page, no JSON target, no
  dialect, no image-to-image). Requiring a token in the constructor is a bug.
- **G2** `LABELZOOM_API_KEY` is the default credential source when none is passed. Explicitly
  passing null or empty **forces** anonymous mode and must not fall back to the environment.

### P — Print

The Cloud Print endpoints, as documented at docs.labelzoom.com under the Cloud Print API. They need
an API key with the `print` scope; an anonymous client still constructs (**G1**), and the server
answers its print calls with 401.

- **P1** Paths, relative to the base URL. Percent-encode each id as a path segment.

  | Call | Request |
  |---|---|
  | Print a document | `POST /api/v3/printers/{printerId}/print` |
  | Print a template | `POST /api/v3/printers/{printerId}/templates/{templateId}/print` |
  | Get a job | `GET /api/v3/jobs/{jobId}` |
  | Get the print quota | `GET /api/v3/print/quota` |

  Every print call sends `Accept: application/json`, because every success response is JSON. The
  convert rule **B2** (`*/*`) doesn't apply. The two GETs send no body and no `Content-Type`.
- **P2** A document print sends the document as the raw body, with its §1.3 `Content-Type`, and
  these query parameters:
  - `sourceFormat`, the source's wire token (**A2**), on every request. The server can't tell ZPL
    from EPL by `Content-Type`, and without it the document goes to the printer unconverted.
  - `copies`, only when the caller sets it.
  - `params`, built exactly as for convert (**C1**–**C4**) from the source options and `rotation`,
    and only when one is set. An empty `params` would make the server convert a document that's
    already in the printer's language.

  Every source format prints except two, which the SDK rejects locally with parameter `source`
  when the printer target is chosen: `url`, because print doesn't fetch, and base64 text, because
  print reads the body as the format's own bytes. Both are reachable only through the run-time
  builders (`from(SourceFormat, ...)`, `fromUrl`, `fromBase64Text`), so the check is a run-time one
  in every language.
- **P3** A template print sends `Content-Type: application/json` and a JSON body built from the
  records: one record is sent as that object, several as an array, and none as `{}`. Query
  parameters: `copies` and `validate=true`, each only when set. It takes no source options:
  a template is already a label.
- **P4** Every print request carries an `Idempotency-Key` header. A key the caller sets is sent
  exactly as given. Otherwise the SDK generates a random UUID v4 per call, and **reuses it on every
  retry of that call**. Without a key, a retry under **F1** after a lost response could print the
  label twice; with one, the server returns the original job instead. A template batch sends one
  key: the server derives each entry's key from it (`key#0`, `key#1`, ...), so the SDK must not.
  The two GETs send no key.
- **P5** Print calls follow **E**, **F** and **G** like convert calls, with **F5** for the daily
  limit.
- **P6** Results:
  - A document print returns a `PrintJob`: `jobId`, `status`, `copies` (null when not set), and
    `idempotent` (true for a replayed request; false when the response omits it).
  - A template print returns a `PrintBatch`, whatever the number of records: `jobs` (a list of
    `PrintJob`), `remaining`, and `message`. `remaining` is the number of records at the end of the
    batch that weren't submitted, 0 when the response omits it; resend the same request with the
    same key to submit them. `message` is null when absent. A single-object response is a batch of
    one job.
  - `get job` returns the job's `id`, `printerId`, `agentId`, `sourceFormat`, `copies`, `status`,
    `error`, `retryCount`, and the `createdDate`, `dispatchedDate`, `completedDate` and
    `retriedDate` timestamps, each null when unset. The wire is snake_case; each SDK uses its own
    naming.
  - `get quota` returns `plan`; `agents` (`used`, `pending`, `limit`, `remaining`); `printers`
    (`used`, `limit`, `remaining`); `jobsToday` (`used`, `limit`, `remaining`, `resetAt`); and
    `limits` (`maxPayloadBytes`, `maxStoredPayloadBytes`, `maxBatchSize`).
  - Keep `status` as a string. The set of job statuses can grow, and a new one must not fail the
    call. Ignore response members the SDK doesn't know.
- **P7** Local validation, with no request sent: `copies` must be a whole number from 1 to 9999;
  `printerId`, `templateId` and `jobId` must be non-empty; a key the caller sets must not be blank;
  the document must not be empty (`body`); each template record must be a JSON object (`data`, as
  **C3**).

### H — Scope

- **H1** `/api/v2.5` streaming is **not** in the fluent surface. It returns 403 for anonymous
  callers ("Streaming APIs are only available to paid users") and is hidden from the OpenAPI spec.
- **H2** Printing is in scope (rule **P**): sending a document or a template to a printer, getting
  a job, and reading the print quota. The rest of `/api/v3` stays out: listing printers and their
  status, listing a printer's jobs, template management and rendering (`/api/v3/templates`), and
  key management (`/api/v3/keys`). So do `/api/v2/labels` (template CRUD). Shape the client so
  `client.printers` and `client.templates` can be added later **without a breaking change**.

---

## 3. Credentials

`Authorization: Bearer <credential>` accepts three shapes. The SDK treats the credential as an
opaque string and must not attempt to parse or validate it:

| Shape | Notes |
|---|---|
| `lz_live_<64 hex>` | static API key, owner's real entitlement |
| `lz_test_<64 hex>` | static API key, always watermarked and unbilled |
| JWT | OAuth or legacy license token |
| *(absent)* | anonymous free tier |

---

## 4. Parameter reference

| Fluent method | Stage | JSON path | Type | Server default | Notes |
|---|---|---|---|---|---|
| `withDpi` | source | `sourceDpi` | int | `203` | the resolution the job or image was made for (**C8**); printer-language and image sources |
| `withDpi` | target | `targetDpi` | int | `sourceDpi`, else `203` | the resolution of the output (**C8**); every target but PDF |
| `withRotation` | target | `rotation` | int | `0` | must be a multiple of 90 (**C6**) |
| `withRaster` { scaling } | source | `scaling` | float | `100` | percent; image and PDF sources |
| `withRaster` { colorMode } | source; image targets | `colorMode` | enum | `GRAYSCALE` | `BW\|GRAYSCALE\|COLOR`; on an image target only `BW` changes the output |
| `withRaster` { darkness } | source; image targets | `darkness` | int | `70` | luminance threshold, 0–100 |
| `withRaster` { position } | source | `position.x` / `position.y` | int | `0,0` | output dots from the label's top-left; negative crops; image or PDF to a printer language |
| `withWatermark` | target | `watermark` | bool | `false` | forced true for unpaid non-PDF→ZPL |
| `withDialect` | target | `dialect` | string | none | e.g. `moca`; **paid** |
| `withData` | source | `data` | array | none | one label per entry (**C3**) |
| `withLabelSize` | source | `label.width` / `label.height` | float | none — server detects | **inches** (**C4**) |
| `withPdfConversionMode` | source | `pdf.conversionMode` | enum | `IMAGE` | `IMAGE\|NATIVE`; PDF source only |
| `withPdfPage` | source | `pdf.pageNumber` | int | all pages | **0-based** (**C4**) |
| `withZplCommandsToIgnore` | source | `zpl.commandsToIgnore` | string[] | `[]` | e.g. `["^PQ"]` |
| `withZplImageCompression` | target | `zpl.imageCompression` | enum | `Z64` | `Z64\|COMPRESSED_HEX`; ZPL target only |
| `withZplCollapseCopies` | target | `zpl.collapseCopies` | bool | `false` | image or PDF → ZPL, `IMAGE` mode: each run of identical labels becomes one label with a `^PQ` count; see [§4.1](#41-multi-label-parameters) |
| `withLayout` { across, gap } | target | `layout.across` / `layout.gap` | int / float | `1` / `0` | image or PDF → ZPL, `IMAGE` mode: labels side by side per printed row; gap in **inches** (**C4**) |
| `withLayout` { fill } | target | `layout.fill` | enum | `SEQUENTIAL` | `SEQUENTIAL\|REPEAT` |
| `withSplit` { rows, columns } | source | `split.rows` / `split.columns` | int | `1` / `1` | image or PDF → ZPL, `IMAGE` mode: cut each page into a grid of labels |
| `withSplit` { width, height } | source | `split.width` / `split.height` | float | none — page divided equally | **inches** on the source page (**C4**) |
| `withSplit` { left, top } | source | `split.left` / `split.top` | float | `0` / `0` | **inches** from the page's top-left corner to the first label |
| `withSplit` { columnGap, rowGap } | source | `split.columnGap` / `split.rowGap` | float | `0` / `0` | **inches** |
| `withSplit` { skipBlank } | source | `split.skipBlank` | bool | `false` | leave out labels with nothing printable at the request's `darkness` |
| `withLabelIndex` | source | `labelIndex` | int | all labels | **0-based** (**C4**); converts only this label, on every plan |

**Stage** is where the option is set in a chaining SDK (§1). Named methods drop the format prefix
on a format-specific stage: `withPdfPage` is `withPage` after `fromPdf`, and
`withZplImageCompression` is `withImageCompression` after `toZpl`. `withSplit` and `withLayout`
are groups (§1.2), shown here with the fields of each row.

Unknown keys are ignored server-side, so a forward-compatible escape hatch costs nothing.

### 4.1 Multi-label parameters

These parameters reshape *how many* labels a conversion returns, so they are where a silent
mistake costs the most: the server ignores a key it does not know, and a misspelled or dropped
option comes back as a valid response with the wrong number of labels in it. That is why each
nested object (`zpl`, `layout`, `split`) must be **merged**, never replaced, when a builder method
sets one of its fields — `request/param-collapse-copies` pins this for `zpl`.

- **`layout`** is the *output* side: `across` labels share one printed row, `gap` inches apart,
  for multi-column roll stock. The response sets `^PW` to the width of the whole row, so
  `label.width` stays the size of **one** label, not the roll. `fill = REPEAT` prints each page
  across a whole row instead of putting the next page in the next column; it is the setting for a
  single image or a one-page file, where `SEQUENTIAL` would leave every column after the first
  blank.
- **`split`** is the *source* side: each page is cut into `rows` × `columns` labels, read left to
  right and then down. Lengths are measured on the page **as a viewer displays it**, page rotation
  included, from its top-left corner, because that is what a caller can measure on the sheet. With
  `width`/`height` unset the page divides equally, assuming the same margin on the far side as
  `left`/`top`. A 1 × 1 grid is no split at all: the other `split` fields are still range-checked,
  but they do not change the output.
- **The cells replace the pages.** From the cut onward, every other parameter sees labels rather
  than pages: `label.*`, `rotation` and `position` apply to each label, `layout` lays labels side
  by side (so a sheet can print onto a 2-across roll), `zpl.collapseCopies` collapses runs of
  identical labels in reading order, and `labelIndex` counts labels.
- **`labelIndex`** selects one label of a multi-label input, and converts it on every plan, which
  is how a free-tier caller reaches any label but the first. It sits at the top level rather than
  under `pdf` because it counts labels, and a split page holds several. For image or PDF → a
  printer-language target it counts labels page by page, within `pdf.pageNumber`'s page when that is
  set. It names a place in the grid, blank or not: `split.skipBlank` only drops blank labels from a
  response that converts them all, so the index a caller reads off the page always means the same
  label, and the server renders only the page that holds it. The response carries `X-Total-Count`
  with every label in scope, and an out-of-range index is a **404** that still carries the count.
- **`zpl.collapseCopies`** is off on the server because it changes the label count of the output,
  so an SDK must not turn it on for a caller (**C1**). It is what turns a marketplace PDF that
  repeats one label per unit into one label and a print quantity.

---

## 5. Documented server limits

- `rotation` must be a multiple of 90
- a `^PQ` greater than 1000 in a ZPL source is reduced to 1000; that does not cap the `^PQ` that
  `zpl.collapseCopies` writes
- maximum rasterizable label is 15in x 15in at 203 dpi
- `layout.across` is 1–10 and `layout.gap` is ≥ 0; `layout.across` > 1 is a **400** for anything
  but image or PDF → a printer-language target in `IMAGE` mode
- `split.rows` and `split.columns` are 1–20 each, with at most 100 labels a page; `split.width` /
  `split.height` are > 0 when set, and the margins and gaps are ≥ 0
- a `split` grid that runs off the page it is checked against (the first page, or the
  `pdf.pageNumber` page when that is set), or any grid other than 1 × 1 outside image or
  PDF → a printer-language target in `IMAGE` mode, is a **400**
- a negative `labelIndex` is a **400**; one past the last label is a **404** with `X-Total-Count`
- one image or PDF → printer-language request may hold at most 20,000 labels (pages × labels per
  page) and 128 MiB of output; past either is a **400**, so a caller with a larger file converts it
  in parts
- `application/x-www-form-urlencoded` and `multipart/form-data` are **not** supported

The SDKs check none of the `layout`, `split` or `labelIndex` limits locally. They are server
policy and can widen without an SDK release; a local check would reject a request the server
accepts, and the server already answers each with an error that names the parameter.

---

## 6. Rate limiting

The gateway rate-limits on two independent keys — client IP, and the credential when present.
On breach it returns **429** with `Retry-After: 60` and a problem document whose `message` (and
`detail`) is `Too many requests. Please try again later.` The body also carries `error` with the
same text, for older readers.

Older servers send `{"error":"Too many requests. Please try again later."}` with no `message`.
**E2**'s extraction order falls through to the raw body there, which is correct and intended. Do
not special-case either shape (`response/error-429-problem-json`, `response/error-429-retry-after`).

---

## 7. Target format behavior

| Target | Media type | Multi-label behavior |
|---|---|---|
| `ZPL` | `text/plain` | all labels concatenated |
| `EPL` `IPL` `TSPL` `DPL` `SBPL` `CPCL` | `text/plain` | all labels concatenated |
| `PDF` | `application/pdf` | all labels, one per page |
| `XML` | `application/xml` | **first label only**, or the one `labelIndex` selects |
| `JSON` | `application/json` | **first label only**, or the one `labelIndex` selects; paid |
| `PNG` `BMP` `GIF` `JPEG` | `image/*` | **first label only**, or the one `labelIndex` selects |

**Printer-language output can carry raw binary inline** — EPL's `GW` and TSPL's `BITMAP` commands
embed a 1-bpp payload directly in the command stream, IPL frames its commands in `STX`/`ETX` and
carries downloaded graphics as packed bitmap columns, and SBPL frames every command with `ESC` and
embeds graphics inline. CPCL is the exception: it writes graphics as hex, so its output is plain
text. `bytes` is authoritative for these targets; `text` decodes with the response
charset and is only safe when you know the label has no graphics. This is the same hazard **D1**
already calls out for image targets, reaching a `text/plain` one.

---

## 8. Versioning

These rules are versioned by `conformance/spec.json`'s `version` field. A change to any rule
requires a matching fixture change, which re-runs every language suite (every workflow includes
`conformance/**` in its path filter). That coupling is deliberate.

---

## 9. Divergences

Approved, deliberate departures from the canonical shape. Anything not listed here is drift.

| Language | Divergence | Rationale |
|---|---|---|
| **Rust** | A `ClientBuilder` for the client and a `ConvertRequest` value struct for the call. No chain. Errors are one `enum Error`, with an `ApiErrorKind` per status rather than a distinct type per status. | Rust has no inheritance, so **E4**'s single base type is an enum: `Err(Error::Api(e))` is the one arm that catches every API error, and `Error::Validation` is a sibling rather than a member for exactly the reason **C6** gives. A chain deferring errors to a terminal `send()` is un-Rust for the same reason the Go row gives, and `Option<T>` + `skip_serializing_if` already expresses **C1** in the type system. |
| **Go** | Functional options for the client, a request struct for the call. No chain. | A fluent chain in Go must either panic or defer errors to a terminal `Do()`; both are un-Go. Functional options + request struct is what every major Go SDK converged on. |
| **Python** | Keyword arguments with `_`-flattened nesting (`label_width`, `pdf_page_number`, `position_x`). Mirrored `AsyncLabelZoomClient`. | Chained builders are not Python. An `options=ConversionOptions(...)` dataclass is the escape hatch for programmatic construction. A second fluent surface is explicitly **not** added for parity — two public APIs doing the same thing doubles the test matrix and the docs for zero users. |
| **Ruby** | Keyword arguments with **nested hashes** (`label: { width: 4 }`), not Python's flattening. | Nested hash literals are idiomatic Ruby in a way `label_width` is not. |
| **TypeScript** | Adds a one-shot form `client.convert({ from, to, body, options })` alongside the chain. | The object-literal call is what most TS users reach for; the chain remains for parity. |
| **Java** | Errors extend `RuntimeException`, not a checked exception. | A checked exception on every `execute()` is hostile inside a fluent chain. |
| **Print, every language without a chain** (Python, Ruby, Go, Rust) | Print follows the language's convert form, with the printer in the target's place: `client.print(source, printer_id, body, copies=2)` beside `client.convert(source, target, body)`, and `client.print_template(template_id, printer_id, data=...)`; `client.Print(ctx, PrintRequest{...})` and `PrintTemplate` in Go, and the request structs in Rust. Getting a job is `jobs.get` in Python and Ruby and `GetPrintJob` / `get_print_job` in Go and Rust. | The same reasons as each language's convert row. Each has a test that sorts every convert option into source or target and asserts print takes the source half plus `rotation`. |
| **Rust** | `PrintQuotaExceeded` is a sibling `ApiErrorKind` variant, not a subtype; `ApiError::is_rate_limited()` is true for both. Job and quota timestamps are RFC 3339 strings. | No inheritance (see the Rust row above). The crate has no date-time dependency, and adding one for four read-only fields isn't worth it. |
| **Go** | Optional strings are `""`, not null: `Code`, `Message`, `Error` and the job ids. `IdempotencyKey: ""` means "generate one". `PrintQuotaExceededError` embeds `RateLimitedError`, so `errors.As` matches it as either. | The SDK already reports an absent `RequestID` as `""`. |
| **PHP** | The error code is `getErrorCode()`. | `\Exception::getCode()` is final and returns an int. |
| **Java** | Absent print result values are `Optional` / `OptionalInt`. | It matches the existing `requestId()`. |

**Never diverge on:** URL construction, header set, `?params=` shape, error taxonomy, retry policy,
request-id surfacing, anonymous mode, 0-based `pdf.pageNumber`, inches for `label.*`.

**Always diverge on:** naming case (`withDpi` / `with_dpi` / `DPI`), error signalling (exception vs.
`(result, err)`), async model, and whether options are chained, kwargs, or a struct.
