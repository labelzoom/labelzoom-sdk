# LabelZoom SDK — API Contract

**Status:** normative. **Applies to:** every language SDK in this repository.

This document states, once, the behavior every LabelZoom SDK must exhibit. It exists because
the SDKs are written independently in seven languages and would otherwise drift. Each rule has
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
             .from(<SourceFormat>, <body>)
             .to(<TargetFormat>)
             .withDpi(int) .withRotation(int) .withScaling(float)
             .withColorMode(BW|GRAYSCALE|COLOR) .withDarkness(int)
             .withPosition(x, y) .withWatermark(bool) .withDialect(string)
             .withData([{...}, ...])
             .withLabelSize(widthInches, heightInches)
             .withPdfConversionMode(IMAGE|NATIVE) .withPdfPage(zeroBasedIndex)
             .withZplCommandsToIgnore([...]) .withZplImageCompression(Z64|COMPRESSED_HEX)
             .execute()
      -> ConversionResult { bytes, text, contentType, status, requestId }
```

### 1.1 Two format types, one builder pair

```
SourceFormat  (13)  ZPL EPL TSPL DPL XML JSON PDF PNG BMP GIF JPEG JPG URL
TargetFormat  (11)  ZPL EPL TSPL DPL XML JSON PDF PNG BMP GIF JPEG
```

`SourceFormat` and `TargetFormat` are **distinct types**, and the sets are not the same. `JPG` and
`URL` are source-only: `JPG` is an input spelling that normalizes to `JPEG` on the wire (**A2**),
and `URL` is a fetch instruction rather than a format — there is no `TargetFormat.URL` to name.
Enforce that at compile time wherever the language allows (C#, TypeScript, Java, PHP 8.1, Go);
`Literal[...]` + mypy in Python and a runtime `ArgumentError` in Ruby are acceptable substitutes.
Do not contort a language to get compile-time enforcement it does not natively offer.

> `EPL`, `TSPL` and `DPL` became targets when the printer-language writers shipped
> (`labelzoom-io-{epl,tspl,dpl}` 1.1.0, wired into the conversion pipeline 2026-08-23). They were
> source-only for the whole of `0.x`, and the SDKs asserted that in four `typecheck/*` cases. Those
> assertions are gone; do not reintroduce them.

There is **one** source builder and **one** target builder. Named convenience methods
(`fromPdf`, `toZpl`) are one-line delegations to `from(SourceFormat.PDF, body)` / `to(TargetFormat.ZPL)`.

> **Why this matters.** The superseded fluent API (PR #6) used a class per format —
> `PdfSourceBuilder`, `ZplSourceBuilder`, `RasterSourceBuilder`, `ZplTargetBuilder`,
> `PdfTargetBuilder`, `RasterTargetBuilder`. Each independently knew the format matrix, and they
> drifted: four `With*` options were silently dropped, `GetSourceFormat()` handled only 2 of 12
> sources, and `RasterTargetBuilder` emitted the *source* type as the target. At 12 x 8 that
> architecture needs ~20 classes and 96 paths nobody can keep consistent. **Do not reintroduce it.**

### 1.2 Format metadata table

Defined **once** per language. Never inlined at a call site.

| SourceFormat | request `Content-Type` | base64/text alternative |
|---|---|---|
| `ZPL` `EPL` `TSPL` `DPL` | `text/plain` | — |
| `XML` | `application/xml` | — |
| `JSON` | `application/json` | — |
| `PDF` | `application/pdf` | `text/plain` |
| `PNG` | `image/png` | `text/plain` |
| `BMP` | `image/bmp` | `text/plain` |
| `GIF` | `image/gif` | `text/plain` |
| `JPEG` / `JPG` | `image/jpeg` | `text/plain` |
| `URL` | `text/plain` (body is the URL) | — |

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

- **B1** `Content-Type` is the exact media type from [§1.2](#12-format-metadata-table). When the
  caller opts into base64/text mode, send `text/plain`.
- **B2** **`Accept: */*`, always.** Never the target's exact media type, never a q-value.

  > The server's `produces` list omits `image/gif`, `image/bmp`, and `image/jpeg`, so Spring's
  > content negotiation rejects an exact `Accept` with **406** before the handler ever runs.
  > Verified against production:
  >
  > ```
  > zpl->gif   Accept: image/gif    -> 406        Accept: */*  -> 200 image/gif
  > zpl->bmp   Accept: image/bmp    -> 406        Accept: */*  -> 200 image/bmp
  > zpl->jpeg  Accept: image/jpeg   -> 406        Accept: */*  -> 200 image/jpeg
  > ```
  >
  > `*/*` is the only value valid for all eleven targets. A `strictAccept` escape hatch may be
  > offered, but it is **off by default**.

- **B3** `Authorization: Bearer <credential>` is sent **only** when a credential is present. When
  absent, the header must be **absent** — never `Bearer `, never `Bearer null`.
- **B4** `User-Agent: labelzoom-<lang>-sdk/<version> (<runtime>)`. It must **not** begin with
  `LabelZoomStudio/` — the server parses that prefix as a Studio version and silently forces
  `pdf.conversionMode = NATIVE` for versions <= 1.8.2.

### C — Parameter serialization

- **C1** Only options the caller **explicitly set** are serialized. Never emit a client-side
  default. `dpi=203` appearing in a request the caller never configured is a bug: it defeats any
  future server-side default change. Implementations need a way to distinguish "unset" from
  "set to the zero value" (pointer fields in Go, a sentinel in Python, nullables elsewhere).
- **C2** All options travel in exactly **one** `?params=<url-encoded JSON>` query parameter. Do
  **not** emit dot-notation (`?dpi=300&label.width=4`).

  > The server accepts both and merges them, but dot-notation cannot express every parameter:
  > `?data=[{},{}]` returns **400**, while `?params={"data":[{},{}]}` returns 200. One
  > serialization path with no per-field special cases is the only maintainable choice.
  > `withRawQueryParam(key, value)` is the escape hatch for anything not yet modeled.

- **C3** `data` is always a JSON **array** of objects, one entry per output label. A single object
  passed by the caller is wrapped into a one-element array. A non-object array element is a local
  validation error.
- **C4** `label.width` / `label.height` are **inches** (float). `pdf.pageNumber` is **0-based** and
  is omitted entirely to convert all pages. Both facts must appear in the docstring of every
  language's corresponding method — they are the two most misread parameters in the API.
- **C5** Enums serialize to exact uppercase wire tokens: `BW|GRAYSCALE|COLOR`, `IMAGE|NATIVE`,
  `Z64|COMPRESSED_HEX`.
- **C6** `rotation` must be a multiple of 90, validated **locally**. This raises a validation error
  type that is **distinct from every API error type** and is never retried.
- **C7** When no options are set, emit no query string at all — a bare URL.

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
  1. JSON `.message` — covers both the gateway shape `{"message": "..."}` and the Spring shape
     `{"timestamp","status","error","message","path"}`
  2. raw body text, truncated to 512 characters
  3. the HTTP reason phrase
- **E3** Errors carry `status`, `message`, `requestId`, and `rawBody`. A 429 additionally carries
  `retryAfterSeconds`, parsed from `Retry-After`.
- **E4** Status maps to type:

  | Status | Type |
  |---|---|
  | 400 | `BadRequest` |
  | 401 | `Unauthorized` |
  | 403 | `Forbidden` |
  | 404 | `NotFound` |
  | 413 | `PayloadTooLarge` |
  | 429 | `RateLimited` |
  | 5xx | `ServerError` |

  All share a single base type so a caller can catch everything with one handler.
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

### G — Anonymous mode

- **G1** Constructing a client with no credential **must succeed**. The API has a working anonymous
  free tier (watermarked, first-label-only, 1 MB body cap, no multi-page, no JSON target, no
  dialect, no image-to-image). Requiring a token in the constructor is a bug.
- **G2** `LABELZOOM_API_KEY` is the default credential source when none is passed. Explicitly
  passing null or empty **forces** anonymous mode and must not fall back to the environment.

### H — Scope

- **H1** `/api/v2.5` streaming is **not** in the fluent surface. It returns 403 for anonymous
  callers ("Streaming APIs are only available to paid users") and is hidden from the OpenAPI spec.
  .NET's pre-existing `PdfToZplAsync` remains as a deprecated shim; that is the sole exception.
- **H2** `/api/v2/labels` (template CRUD), `/api/v3/printers` (cloud print), and `/api/v3/keys`
  (key management) are out of scope for v1. Shape the client so `client.labels` / `client.printers`
  can be added later **without a breaking change**.

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

| Fluent method | JSON path | Type | Server default | Notes |
|---|---|---|---|---|
| `withDpi` | `dpi` | int | `203` | |
| `withRotation` | `rotation` | int | `0` | must be a multiple of 90 (**C6**) |
| `withScaling` | `scaling` | float | `100` | percent |
| `withColorMode` | `colorMode` | enum | `GRAYSCALE` | `BW\|GRAYSCALE\|COLOR` |
| `withDarkness` | `darkness` | int | `70` | luminance threshold, 0–100 |
| `withPosition` | `position.x` / `position.y` | int | `0,0` | pixel offset of extraction corner |
| `withWatermark` | `watermark` | bool | `false` | forced true for unpaid non-PDF→ZPL |
| `withDialect` | `dialect` | string | none | e.g. `moca`; **paid** |
| `withData` | `data` | array | none | one label per entry (**C3**) |
| `withLabelSize` | `label.width` / `label.height` | float | none | **inches** (**C4**) |
| `withPdfConversionMode` | `pdf.conversionMode` | enum | `IMAGE` | `IMAGE\|NATIVE`; PDF source only |
| `withPdfPage` | `pdf.pageNumber` | int | all pages | **0-based** (**C4**) |
| `withZplCommandsToIgnore` | `zpl.commandsToIgnore` | string[] | `[]` | e.g. `["^PQ"]` |
| `withZplImageCompression` | `zpl.imageCompression` | enum | `Z64` | `Z64\|COMPRESSED_HEX`; ZPL target only |

Unknown keys are ignored server-side, so a forward-compatible escape hatch costs nothing.

---

## 5. Documented server limits

- `rotation` must be a multiple of 90
- `^PQ` values greater than 1000 are rejected
- maximum rasterizable label is 15in x 15in at 203 dpi
- anonymous free-tier request body cap is 1 MB
- `application/x-www-form-urlencoded` and `multipart/form-data` are **not** supported

---

## 6. Rate limiting

The gateway rate-limits on two independent keys — client IP, and the credential when present.
On breach it returns **429** with `Retry-After: 60` and body
`{"error":"Too many requests. Please try again later."}`.

Note that this body uses `error`, not `message`. **E2**'s extraction order falls through to the raw
body here, which is correct and intended — do not special-case it.

---

## 7. Target format behavior

| Target | Media type | Multi-label behavior |
|---|---|---|
| `ZPL` | `text/plain` | all labels concatenated |
| `EPL` `TSPL` `DPL` | `text/plain` | all labels concatenated |
| `PDF` | `application/pdf` | all labels, one per page |
| `XML` | `application/xml` | **first label only** |
| `JSON` | `application/json` | **first label only**; paid |
| `PNG` `BMP` `GIF` `JPEG` | `image/*` | **first label only** |

**`EPL` and `TSPL` output can carry raw binary inline** — EPL's `GW` and TSPL's `BITMAP` commands
embed a 1-bpp payload directly in the command stream. `bytes` is authoritative for these targets;
`text` decodes with the response charset and is only safe when you know the label has no graphics.
This is the same hazard **D1** already calls out for image targets, reaching a `text/plain` one.

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
| **Go** | Functional options for the client, a request struct for the call. No chain. | A fluent chain in Go must either panic or defer errors to a terminal `Do()`; both are un-Go. Functional options + request struct is what every major Go SDK converged on. |
| **Python** | Keyword arguments with `_`-flattened nesting (`label_width`, `pdf_page_number`, `position_x`). Mirrored `AsyncLabelZoomClient`. | Chained builders are not Python. An `options=ConversionOptions(...)` dataclass is the escape hatch for programmatic construction. A second fluent surface is explicitly **not** added for parity — two public APIs doing the same thing doubles the test matrix and the docs for zero users. |
| **Ruby** | Keyword arguments with **nested hashes** (`label: { width: 4 }`), not Python's flattening. | Nested hash literals are idiomatic Ruby in a way `label_width` is not. |
| **TypeScript** | Adds a one-shot form `client.convert({ from, to, body, options })` alongside the chain. | The object-literal call is what most TS users reach for; the chain remains for parity. |
| **Java** | Errors extend `RuntimeException`, not a checked exception. | A checked exception on every `execute()` is hostile inside a fluent chain. |

**Never diverge on:** URL construction, header set, `?params=` shape, error taxonomy, retry policy,
request-id surfacing, anonymous mode, 0-based `pdf.pageNumber`, inches for `label.*`.

**Always diverge on:** naming case (`withDpi` / `with_dpi` / `DPI`), error signalling (exception vs.
`(result, err)`), async model, and whether options are chained, kwargs, or a struct.
