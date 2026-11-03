![LabelZoom Logo](../docs/LabelZoom_Logo_f_400px.png)

# LabelZoom Node.js / TypeScript SDK

Official Node client for the [LabelZoom API](https://api.labelzoom.com). Converts barcode labels
between ZPL, EPL, IPL, TSPL, DPL, SBPL, PDF, LabelZoom XML/JSON, and raster images.

Node 20+. Ships dual ESM/CJS builds and TypeScript declarations. No runtime dependencies — it
uses the platform `fetch`.

## Install

```sh
npm install @labelzoom/sdk
```

Published from CI with [npm provenance](https://docs.npmjs.com/generating-provenance-statements),
so every release is cryptographically linked to the commit and workflow that built it.

> **Stable.** The public API is covered by a shared conformance suite that every language SDK
> runs against the same fixtures. Each SDK is versioned independently, and the contract carries
> its own version in `conformance/spec.json`.

<details>
<summary>Build from source</summary>

```sh
git clone https://github.com/labelzoom/labelzoom-sdk.git
cd labelzoom-sdk/node
npm install && npm run build
```
</details>

## Quick start

**An API key is optional.** Without one you get the free tier — watermarked output, first label
only, a 1 MB request cap, and no multi-page, JSON-target, or image-to-image conversion.

```ts
import { LabelZoomClient } from '@labelzoom/sdk';

const client = new LabelZoomClient();            // anonymous; this works

const result = await client.convert({
  from: 'zpl',
  to: 'png',
  body: '^XA^FO20,20^A0N,28^FDHello^FS^XZ',
  options: { dpi: 300, label: { width: 4, height: 6 } },
});

await writeFile('label.png', result.bytes);
```

Or as a chain, matching the other LabelZoom SDKs:

```ts
const result = await client.convert()
  .fromZpl('^XA^FO20,20^A0N,28^FDHello^FS^XZ')
    .withLabelSize(4, 6)
  .toPng()
    .withDpi(300)
  .execute();
```

Both forms send the same request. The object form is what most TypeScript users reach for; the
chain exists for parity with the .NET, Java, and PHP SDKs.

The chain has two stages. `from*` picks the source and returns the **source stage**, where you set
options that change how the source is read. `to*` picks the target and returns the **target
stage**, where you set options that change the output and then call `execute`. Each stage offers
only the options that act on its side, and a named `from*` or `to*` offers only the options for
that format, so `fromZpl(zpl).withPage(0)` is a compile error.

A Letter sheet of four labels, printed two across on a 2-up roll:

```ts
const result = await client.convert()
  .fromPdf(sheet)
    .withSplit({ rows: 2, columns: 2, skipBlank: true })
  .toZpl()
    .withLayout({ across: 2, gap: 0.125 })      // inches
  .execute();
```

With a key — passed explicitly, or picked up from `LABELZOOM_API_KEY`:

```ts
const client = new LabelZoomClient({ apiKey: 'lz_live_...' });
const fromEnv = new LabelZoomClient();           // reads LABELZOOM_API_KEY
const anonymous = new LabelZoomClient({ apiKey: '' });  // forces anonymous
```

Filling variable fields — **each record produces one label**:

```ts
const result = await client.convert()
  .fromZpl(template)
    .withData({ name: 'ACME Corp', sku: '12345' }, { name: 'Globex', sku: '67890' })
  .toPdf()
  .execute();                                    // a 2-page PDF
```

## Formats

```ts
type SourceFormat = 'zpl' | 'epl' | 'ipl' | 'tspl' | 'dpl' | 'sbpl'
                  | 'xml' | 'json' | 'pdf' | 'png' | 'bmp' | 'gif'
                  | 'jpeg' | 'jpg' | 'url';

type TargetFormat = 'zpl' | 'epl' | 'ipl' | 'tspl' | 'dpl' | 'sbpl'
                  | 'xml' | 'json' | 'pdf' | 'png' | 'bmp' | 'gif'
                  | 'jpeg';
```

`jpg` and `url` are **source-only** — `jpg` normalizes to `jpeg`, and `url` is a fetch instruction
rather than a format — and the type system says so:

```ts
await client.convert({ from: 'pdf', to: 'url', body });
//                                      ~~~~~ Type '"url"' is not assignable to 'TargetFormat'
```

A compile error, not a runtime 404.

`epl`, `ipl`, `tspl`, `dpl` and `sbpl` are targets as well as sources — ``{ from: 'pdf', to: 'epl' }`` is a real conversion. Their
output is `text/plain` with every label concatenated, but they can all carry raw binary (EPL's `GW`,
TSPL's `BITMAP`, IPL's `STX`/`ETX`-framed bitmap columns, SBPL's inline graphics): read `result.bytes` rather than `result.text` whenever a label might carry graphics.

`from: 'url'` has the *server* fetch a URL you supply and convert what it finds. Validate the URL
first if it came from untrusted input.

## Options

| Option | Notes |
|---|---|
| `dpi` | server default 203 |
| `rotation` | must be a multiple of 90; rejected locally otherwise |
| `scaling` | percent, server default 100 |
| `colorMode` | `'BW'`, `'GRAYSCALE'` (default), `'COLOR'` |
| `darkness` | 0–100, server default 70 |
| `position` | `{ x, y }` pixel offset of the extracted region |
| `watermark` | forced on for the free tier regardless |
| `dialect` | e.g. `'moca'`; **paid** |
| `label` | `{ width, height }` in **inches**, not dots |
| `pdf.conversionMode` | `'IMAGE'` (default) or `'NATIVE'` |
| `pdf.pageNumber` | **0-based**; omit to convert every page |
| `zpl.commandsToIgnore` | e.g. `['^PQ']` |
| `zpl.imageCompression` | `'Z64'` (default) or `'COMPRESSED_HEX'` |
| `zpl.collapseCopies` | image or PDF to ZPL, `IMAGE` mode: each run of identical labels becomes one label with a `^PQ` count |
| `layout` | image or PDF to ZPL, `IMAGE` mode: `{ across, gap, fill }`, labels side by side per row; `gap` in **inches**; `fill` is `'SEQUENTIAL'` (default) or `'REPEAT'` |
| `split` | image or PDF to ZPL, `IMAGE` mode: `{ rows, columns, width, height, left, top, columnGap, rowGap, skipBlank }` cuts each page into a grid of labels; lengths in **inches**; omit `width`/`height` to divide the page equally |
| `labelIndex` | **0-based**; converts only this label, on every plan |
| `data` | one label per record |

Only options you actually set are sent. The SDK never fills in a client-side default, so a change
to a server default reaches you without an SDK upgrade. Unknown keys are ignored server-side, so
the index signature on `ConversionOptions` is a safe forward-compatibility escape hatch.

### Chain methods by stage

| Stage | Method | On |
|---|---|---|
| Source | `withLabelSize`, `withLabelIndex`, `withData` | every source |
| Source | `withPage`, `withConversionMode` | `fromPdf` |
| Source | `withSplit({ … })` | `fromPdf`, `fromPng`, `fromBmp`, `fromGif`, `fromJpeg` |
| Source | `withCommandsToIgnore` | `fromZpl` |
| Target | `withDpi`, `withRotation`, `withScaling`, `withColorMode`, `withDarkness`, `withPosition`, `withWatermark`, `withDialect`, `withParameter` | every target |
| Target | `withImageCompression`, `withCollapseCopies`, `withLayout({ … })` | `toZpl` |

`from(format, body)`, `fromBase64Text` and `fromUrl` return a `DynamicSourceBuilder`, and
`to(target)` returns a `DynamicTargetBuilder`. The compiler can't know the format there, so they
offer every option, named with the format it applies to: `withPdfPage`, `withPdfConversionMode`,
`withZplCommandsToIgnore`, `withZplImageCompression` and `withZplCollapseCopies`.

### Migrating from 1.x

The object form (`client.convert({ from, to, body, options })`) is unchanged. In the chain, every
option used to be set after `to*`. In 2.0, move the source options before it:

| 1.x | 2.0 |
|---|---|
| `.toZpl().withPdfPage(0)` | `.fromPdf(pdf).withPage(0).toZpl()` |
| `.withPdfConversionMode(m)` | `.fromPdf(pdf).withConversionMode(m)` |
| `.withZplCommandsToIgnore('^PQ')` | `.fromZpl(zpl).withCommandsToIgnore('^PQ')` |
| `.withLabelSize(w, h)`, `.withLabelIndex(i)`, `.withData(...)` | same name, before `to*` |
| `.withZplImageCompression(c)` | `.toZpl().withImageCompression(c)` |
| `ConversionSourceBuilder`, `ConversionTargetBuilder` | `SourceStage` and `TargetStage` subclasses, such as `PdfSourceBuilder` and `ZplTargetBuilder` |

## Errors

Every non-2xx rejects with a typed error carrying the server's own message, the raw body, and the
`X-LZ-Request-Id` support handle. The body is never discarded.

```ts
import { LabelZoomError, ForbiddenError } from '@labelzoom/sdk';

try {
  await client.convert({ from: 'zpl', to: 'json', body: zpl });
} catch (error) {
  if (error instanceof ForbiddenError && error.isPaidFeature) {
    // "JSON export is a paid feature" — the most common free-tier failure.
    console.error(`${error.message} (request ${error.requestId})`);
  } else if (error instanceof LabelZoomError) {
    console.error(`${error.status}: ${error.message}`);
  }
}
```

`BadRequestError`, `UnauthorizedError`, `ForbiddenError`, `NotFoundError`,
`PayloadTooLargeError`, `RateLimitedError` and `ServerError` all extend `LabelZoomError`.

`LabelZoomValidationError` deliberately does **not** — it means the calling code is wrong, it
never reaches the network, and it should not be swallowed by a handler written for server failures.

## Retries

429, 5xx and transport failures are retried automatically: 3 attempts, 1s/2s/4s with full jitter,
honoring a longer `Retry-After`. Other 4xx responses reject immediately — a malformed request
will not become valid on a second attempt.

```ts
const client = new LabelZoomClient({ maxRetries: 0, timeoutMs: 30_000 });
```

## Testing your own code

`fetch` is injectable, and `sleep`/`useJitter` let retry logic be tested without spending the
wall-clock time:

```ts
const client = new LabelZoomClient({
  fetch: yourStub,
  useJitter: false,
  sleep: async (ms) => { recorded.push(ms); },
});
```

## Development

```sh
npm install
npm test          # offline; no key, no network
npm run typecheck
npm run build     # dual ESM/CJS + .d.ts into dist/
```

The test suite runs the shared [conformance fixtures](../conformance/) that every LabelZoom SDK is
checked against, and asserts it executed all of them. See
[docs/CONFORMANCE.md](../docs/CONFORMANCE.md).

## License

MIT — see [LICENSE](../LICENSE).
