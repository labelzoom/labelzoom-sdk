![LabelZoom Logo](../docs/LabelZoom_Logo_f_400px.png)

# LabelZoom Node.js / TypeScript SDK

The official Node.js client for the [LabelZoom API](https://api.labelzoom.com). Use it to convert
barcode labels between ZPL, EPL, IPL, TSPL, DPL, SBPL, CPCL, PDF, LabelZoom XML and JSON, and raster
images, to read ESC/Label, Fingerprint, EZPL, TPCL, NiceLabel, BarTender, and DYMO files, and to
print labels through LabelZoom Cloud Print.

## What the SDK handles

- Retries `429`, `5xx`, and transport failures with backoff, and honors a longer `Retry-After`.
- Rejects every non-2xx response with a typed error that carries the status, the server's message,
  the error `code`, the request ID, and the raw body.
- Returns every result as bytes, so binary output such as PDF, PNG, and EPL graphics arrives intact.
- Sends an `Idempotency-Key` with every print, so a retried print doesn't print twice.
- Works without an API key, on the watermarked free tier. See
  [Rate limits and plans](https://docs.labelzoom.com/reference/rate-limits-and-plans/).
- Ships ESM and CommonJS builds with TypeScript declarations, and has no runtime dependencies: it
  uses the platform `fetch`.

## Install

The SDK requires Node.js 20 or later.

```sh
npm install @labelzoom/sdk
```

Each release is published from CI with
[npm provenance](https://docs.npmjs.com/generating-provenance-statements), which links the package
to the commit and workflow that built it.

## Convert a label

A conversion is a pipeline. You choose the source with a `from*` method, set source options, choose
the target with a `to*` method, set target options, and then call `execute`. This program converts
page 0 of a PDF to a 4 x 6 inch ZPL label for a 203 dpi printer:

```ts
import { readFile, writeFile } from 'node:fs/promises';
import { LabelZoomClient } from '@labelzoom/sdk';

const client = new LabelZoomClient();            // reads LABELZOOM_API_KEY

const result = await client.convert()
  .fromPdf(await readFile('shipping-label.pdf'))
    .withPage(0)                                 // source option: 0-based page
    .withLabelSize(4, 6)                         // source option: inches
  .toZpl()
    .withDpi(203)                                // target option: output resolution
  .execute();

await writeFile('label.zpl', result.bytes);
```

Source options change how the document is read: its label size, which page or label to convert,
the data that fills its variable fields, and the resolution it was made for. Target options change
what is written: the output resolution, rotation, watermark, and format-specific settings. Each
stage offers only the options that act there, and a named `from*` or `to*` method offers only the
options for that format, so `fromZpl(zpl).withPage(0)` and `toPdf().withDpi(300)` are compile
errors. For the full lists, see [Options by stage](#options-by-stage).

The same conversion also works as one call that takes a flat `options` object. The object form
sends the same request, but the compiler doesn't check which stage or format an option belongs to:

```ts
const result = await client.convert({
  from: 'pdf',
  to: 'zpl',
  body: await readFile('shipping-label.pdf'),
  options: { pdf: { pageNumber: 0 }, label: { width: 4, height: 6 }, targetDpi: 203 },
});
```

`result` holds `bytes`, `text` (the bytes decoded with the response charset, else UTF-8),
`contentType`, `status`, and `requestId`.

### Authenticate

An API key is optional. Without one, you get the free tier: watermarked output, the first label
only, a 1 MB request limit, and no multi-page, JSON-target, or image-to-image conversion.

```ts
const client = new LabelZoomClient({ apiKey: 'API_KEY' });
const fromEnv = new LabelZoomClient();                  // reads LABELZOOM_API_KEY
const anonymous = new LabelZoomClient({ apiKey: '' });  // ignores LABELZOOM_API_KEY
```

The key can be an `lz_live_` or `lz_test_` key, or a JWT. `client.isAuthenticated` reports whether
the client resolved a credential. Create one client and reuse it.

### Fill variable fields

Each record produces one label:

```ts
const result = await client.convert()
  .fromZpl(template)
    .withData({ name: 'ACME Corp', sku: '12345' }, { name: 'Globex', sku: '67890' })
  .toPdf()
  .execute();                                    // a 2-page PDF
```

## Print a label

A printer is a target. Use `toPrinter(printerId)` where you would use `toZpl()`. The printer's
configuration sets its language, and LabelZoom converts the document to it, so a PDF prints on a ZPL
printer. Printing needs an API key with the `print` scope, and a printer connected through the
LabelZoom Print Agent.

To print a document and a template, and then check on the job:

```ts
const job = await client.convert()
  .fromPdf(pdf)
    .withPage(0)                                 // any source option
  .toPrinter(printerId)
    .withCopies(2)
    .withIdempotencyKey(orderNumber)
  .execute();                                    // { jobId, status, copies, idempotent }

const batch = await client.convert()
  .fromTemplate(templateId)
    .withData({ orderNumber: 'SO-1001', sku: 'A-100' })
    .withValidate()                              // check every record for missing fields first
  .toPrinter(printerId)
    .withIdempotencyKey(`${orderNumber}-labels`)
  .execute();                                    // { jobs, remaining, message }

const details = await client.jobs.get(job.jobId);
console.log(details.status);                     // queued, dispatched, printing, completed, or failed

const quota = await client.getPrintQuota();
console.log(quota.jobsToday.remaining);
```

The one-call forms take the same fields:

```ts
const job = await client.print({
  printerId,
  from: 'pdf',
  body: pdf,
  options: { pdf: { pageNumber: 0 } },
  copies: 2,
  idempotencyKey: orderNumber,
});

const batch = await client.printTemplate({
  printerId,
  templateId,
  data: { orderNumber: 'SO-1001', sku: 'A-100' },
  validate: true,
});
```

- Every source format prints, including NiceLabel, BarTender, and DYMO files. A URL and base64 text
  can't print: fetch or decode the document and print its bytes.
- The printer target offers `withRotation`. Other target options depend on the output language, and
  the printer sets that.
- `withCopies(n)` sends the whole job `n` times, from 1 to 9999. It multiplies a quantity in the
  document: `^PQ3` with 5 copies prints 15 labels. For a template, it sends each label `n` times.
- Every print carries an `Idempotency-Key`. Set your own with `withIdempotencyKey`, such as an order
  number, to make a repeated call return the original job instead of printing again. A deliberate
  reprint needs a new key. Without one, the SDK generates a key per call and reuses it on that
  call's retries. A template print sends one key for the whole batch.
- A template batch that runs out of time returns `remaining`, the number of records not yet
  submitted, and a `message`. To submit them, send the same request with the same key.
- `client.jobs.get(jobId)` returns the job's `status`, `error`, `copies`, `retryCount`, and its
  created, dispatched, completed, and retried dates. Job history is kept for 30 days. Treat `status`
  as an open set of strings.
- `client.getPrintQuota()` returns your plan, your agents, printers, and today's jobs (`used`,
  `limit`, and `remaining`), and the payload and batch limits. `jobsToday.resetAt` is 00:00 UTC.
- The daily job limit rejects with `PrintQuotaExceededError`, which the SDK never retries. Check
  `getPrintQuota()` before a large run.

## Formats

```ts
type SourceFormat = 'zpl' | 'epl' | 'ipl' | 'tspl' | 'dpl' | 'sbpl'
                  | 'esclabel' | 'fingerprint' | 'ezpl' | 'cpcl' | 'tpcl'
                  | 'nlbl' | 'lbl' | 'btw' | 'dymo'
                  | 'xml' | 'json' | 'pdf' | 'png' | 'bmp' | 'gif'
                  | 'jpeg' | 'jpg' | 'url';

type TargetFormat = 'zpl' | 'epl' | 'ipl' | 'tspl' | 'dpl' | 'sbpl' | 'cpcl'
                  | 'xml' | 'json' | 'pdf' | 'png' | 'bmp' | 'gif'
                  | 'jpeg';
```

- ESC/Label, Fingerprint, EZPL, TPCL, NiceLabel (`nlbl`, `lbl`), BarTender (`btw`), and DYMO are
  source-only. Send NiceLabel and BarTender files as bytes, not text.
- `jpg` and `url` are source-only too. `jpg` normalizes to `jpeg`, and `url` is a fetch
  instruction rather than a format. A target of `'url'` is a compile error:

  ```ts
  await client.convert({ from: 'pdf', to: 'url', body });
  //                                      ~~~~~ Type '"url"' is not assignable to 'TargetFormat'
  ```

- `from: 'url'` (or `fromUrl`) has the server fetch the URL you supply and convert what it finds.
  Validate the URL first if it came from untrusted input.
- `epl`, `ipl`, `tspl`, `dpl`, `sbpl`, and `cpcl` are targets as well as sources:
  `{ from: 'pdf', to: 'epl' }` is a real conversion. Their output is `text/plain` with every label
  concatenated, but EPL, IPL, TSPL, and SBPL can carry raw binary (EPL's `GW`, TSPL's `BITMAP`,
  IPL's `STX`/`ETX`-framed bitmap columns, SBPL's inline graphics). Read `result.bytes` rather than
  `result.text` whenever a label might carry graphics. CPCL writes graphics as hex, so its output is
  plain text.
- PDF output holds every label, one per page. XML, JSON, and image targets return the first label
  only, or the one `withLabelIndex` selects.

## Options by stage

The SDK sends only the options you set and never fills in a client-side default, so a change to a
server default reaches you without an SDK upgrade. In the object form, each option is a key of
`ConversionOptions`.

### Source options

| Chain method | `options` key | On | What it does |
| --- | --- | --- | --- |
| `withLabelSize(width, height)` | `label: { width, height }` | Every source | The label size in inches, not dots. It replaces the size the source declares. Omit it to use that size, or to detect the size from the content when the source declares none. |
| `withLabelIndex(index)` | `labelIndex` | Every source | The 0-based index of the one label to convert, on every plan. For an image or PDF, it counts labels after any split, page by page. |
| `withData(...records)` | `data` | Every source | One label per record. A single record produces a single label. |
| `withDpi(dpi)` | `sourceDpi` | Printer-language and image sources | The resolution the source was made for: how its dots or pixels map to inches. Server default 203. Not offered for PDF, LabelZoom XML and JSON, NiceLabel, BarTender, or DYMO sources. |
| `withPage(page)` | `pdf.pageNumber` | `fromPdf` | The 0-based page to convert. Omit it to convert every page. |
| `withConversionMode(mode)` | `pdf.conversionMode` | `fromPdf` | `'IMAGE'` (default) or `'NATIVE'`. Splitting and layout need `IMAGE`. |
| `withCommandsToIgnore(...commands)` | `zpl.commandsToIgnore` | `fromZpl`, `fromEscLabel` | ZPL commands to skip, such as `'^PQ'`. |
| `withSplit({ ... })` | `split` | `fromPdf`, `fromPng`, `fromBmp`, `fromGif`, `fromJpeg` | Cuts each page into a grid of labels, read left to right and then down. ZPL target in `IMAGE` mode only. Fields: `rows` and `columns` (1 to 20 each), `width`, `height`, `left`, `top`, `columnGap`, and `rowGap`, all in inches, and `skipBlank`. Omit `width` and `height` to divide the page equally. |
| `withRaster({ ... })` | `scaling`, `position`, `darkness`, `colorMode` | `fromPdf`, `fromPng`, `fromBmp`, `fromGif`, `fromJpeg` | How the picture is rasterized. `scaling` is a percent (server default 100). `position` is `{ x, y }` in output dots from the label's top-left corner, and a negative value crops. `darkness` is the black-and-white threshold from 0 to 100 (server default 70). `colorMode` is `'BW'`, `'GRAYSCALE'` (default), or `'COLOR'`. In `NATIVE` mode, only `scaling` applies. |

To crop a picture, set a negative position and the size of the label it's cut to:

```ts
await client.convert()
  .fromPng(photo)
    .withLabelSize(4, 6)
    .withRaster({ position: { x: -120, y: -80 } })   // trims 120 dots off the left, 80 off the top
  .toZpl()
  .execute();
```

### Target options

| Chain method | `options` key | On | What it does |
| --- | --- | --- | --- |
| `withDpi(dpi)` | `targetDpi` | Every target but `toPdf` | The resolution of the output. Server default: the source's resolution, else 203. |
| `withRotation(degrees)` | `rotation` | Every target, and `toPrinter` | Degrees clockwise. Must be a multiple of 90; the SDK rejects any other value locally. |
| `withWatermark(watermark)` | `watermark` | Every target | The free tier forces it on. |
| `withDialect(dialect)` | `dialect` | Every target | Such as `'moca'`. Needs a paid plan. |
| `withParameter(key, value)` | Any key | Every target | Sends an option the SDK doesn't model. The server ignores keys it doesn't know. |
| `withImageCompression(compression)` | `zpl.imageCompression` | `toZpl` | `'Z64'` (default) or `'COMPRESSED_HEX'`. |
| `withCollapseCopies(collapse)` | `zpl.collapseCopies` | `toZpl` | Image or PDF source in `IMAGE` mode: each run of identical labels becomes one label with a `^PQ` count. Server default `false`. |
| `withLayout({ ... })` | `layout` | `toZpl` | Image or PDF source in `IMAGE` mode: puts labels side by side in each printed row, for multi-column roll stock. Fields: `across` (1 to 10), `gap` in inches, and `fill`, which is `'SEQUENTIAL'` (default) or `'REPEAT'`. |
| `withColorMode(mode)` | `colorMode` | `toPng`, `toBmp`, `toGif`, `toJpeg` | `'BW'` renders a black-and-white image at the `withDarkness` threshold. Any other value renders in color. |
| `withDarkness(darkness)` | `darkness` | `toPng`, `toBmp`, `toGif`, `toJpeg` | The black-and-white threshold, from 0 to 100. Server default 70. |

`colorMode` and `darkness` are one key each, shared by `withRaster` and the image target stage. If
you set them on both stages, the last value set is sent.

A Letter sheet of four labels, printed two across on a 2-up roll:

```ts
const result = await client.convert()
  .fromPdf(sheet)
    .withSplit({ rows: 2, columns: 2, skipBlank: true })
  .toZpl()
    .withLayout({ across: 2, gap: 0.125 })      // inches
  .execute();
```

### Choose the format at run time

`from(format, body)`, `fromBase64Text(format, base64)`, and `fromUrl(url)` return a
`DynamicSourceBuilder`, and `to(target)` returns a `DynamicTargetBuilder`. The compiler can't know
the format there, so they offer every option, named with the format it applies to:
`withPdfPage`, `withPdfConversionMode`, `withZplCommandsToIgnore`, `withZplImageCompression`, and
`withZplCollapseCopies`, plus `withDpi`, `withSplit`, `withRaster`, `withLayout`, `withColorMode`,
and `withDarkness`. The server ignores an option that doesn't apply to the format.

```ts
const result = await client.convert()
  .from(sourceFormat, body)
    .withPdfPage(0)
  .to(targetFormat)
  .execute();
```

## Errors

Every non-2xx response rejects with a subclass of `LabelZoomError`:

| Class | Status | Notes |
| --- | --- | --- |
| `BadRequestError` | 400 | The request is malformed or the conversion path is invalid. |
| `UnauthorizedError` | 401 | The credential was rejected. |
| `ForbiddenError` | 403 | `isPaidFeature` is `true` when the 403 is a paid feature, such as a JSON target on the free tier. |
| `NotFoundError` | 404 | The conversion path doesn't exist, or the label index is past the last label. |
| `PayloadTooLargeError` | 413 | The body is over your plan's limit: 1 MB on the free tier. |
| `UnprocessableError` | 422 | Well formed, but it can't be carried out. Print returns it with a `code` such as `agent_update_required` or `idempotency_key_reused`, and for a template record with missing fields. |
| `RateLimitedError` | 429 | `retryAfterSeconds` holds `Retry-After`, when the server sent it. |
| `PrintQuotaExceededError` | 429 | The daily print job limit, code `daily_job_limit_reached`. Extends `RateLimitedError`. |
| `ServerError` | 5xx | Thrown after the retries run out. |
| `LabelZoomError` | Any other | The base class. Catch it to handle every API error. |

Every API error carries `status`, `message` (the server's message, up to 512 characters), `code`
(the error's stable code, or `null`), `requestId` (the `X-LZ-Request-Id` header, which LabelZoom
support asks for), and `rawBody` (the full response body). Branch on `code` rather than on
`message`.

```ts
import { ForbiddenError, LabelZoomError } from '@labelzoom/sdk';

try {
  await client.convert({ from: 'zpl', to: 'json', body: zpl });
} catch (error) {
  if (error instanceof ForbiddenError && error.isPaidFeature) {
    // "JSON export is a paid feature", the most common free-tier error.
    console.error(`${error.message} (request ${error.requestId})`);
  } else if (error instanceof LabelZoomError) {
    console.error(`${error.status} ${error.code ?? ''}: ${error.message}`);
  } else {
    throw error;
  }
}
```

`LabelZoomValidationError` doesn't extend `LabelZoomError`. It means the calling code is wrong,
such as a rotation of 45, an empty body, or copies outside 1 to 9999. The SDK throws it before
sending anything, never retries it, and names the option at fault in `parameter`.

## Retries

The SDK retries `429`, `5xx`, and transport failures: 3 attempts in all, waiting 1 s, 2 s, and 4 s
with full jitter, or longer when `Retry-After` asks for longer. The daily print limit is the
exception: its `429` rejects at once, because the limit resets at 00:00 UTC. Other `4xx` responses
reject without a retry, and so does a request you cancel with an `AbortSignal`.

To disable retries and set a timeout for each attempt:

```ts
const client = new LabelZoomClient({ maxRetries: 0, timeoutMs: 30_000 });
```

## Configure the client

| Option | Default | What it does |
| --- | --- | --- |
| `apiKey` | `LABELZOOM_API_KEY` | An `lz_live_` or `lz_test_` key, or a JWT. Pass `''` to force anonymous requests. |
| `baseUrl` | `https://api.labelzoom.com` | The API host. A path prefix is kept, for a reverse proxy. |
| `maxRetries` | `2` | Retries after the first attempt. `0` disables them. |
| `timeoutMs` | None | The timeout for each attempt, in milliseconds. |
| `userAgentSuffix` | None | Text appended to the SDK's `User-Agent`. |
| `fetch` | `globalThis.fetch` | The `fetch` implementation to use. |
| `useJitter` | `true` | Randomizes the retry delay. |
| `sleep` | `setTimeout` | Waits between retries. |

To cancel a request, pass an `AbortSignal`: as the argument to `execute`, `jobs.get`, or
`getPrintQuota`, or as `signal` in the request object of `convert`, `print`, or `printTemplate`.

## Test your own code

`fetch` is injectable, and `sleep` and `useJitter` let you test retry logic without waiting:

```ts
const client = new LabelZoomClient({
  fetch: yourStub,
  useJitter: false,
  sleep: async (ms) => { recorded.push(ms); },
});
```

## Migrate from 1.x

In 1.x, every chain option was set after `to*`. In 2.0, source options come before `to*`, and the
single resolution splits into a source and a target resolution.

| 1.x | 2.0 |
| --- | --- |
| `.toZpl().withPdfPage(0)` | `.fromPdf(pdf).withPage(0).toZpl()` |
| `.withPdfConversionMode(m)` | `.fromPdf(pdf).withConversionMode(m)` |
| `.withZplCommandsToIgnore('^PQ')` | `.fromZpl(zpl).withCommandsToIgnore('^PQ')` |
| `.withLabelSize(w, h)`, `.withData(...)` | The same names, before `to*` |
| `.withZplImageCompression(c)` | `.toZpl().withImageCompression(c)` |
| `.withDpi(d)`, which set both resolutions | `.withDpi(d)` after `to*` for the output resolution, and also before it if the source was made for `d` |
| `options.dpi` in the object form | `options.targetDpi`, and `options.sourceDpi` if the source was made for that resolution |
| `.withScaling(p)`, `.withPosition(x, y)` | `.withRaster({ scaling: p, position: { x, y } })` after `fromPdf` or an image `from*` |
| `.withDarkness(d)`, `.withColorMode(m)` | `withRaster` for an image or PDF source; `withDarkness` and `withColorMode` after an image `to*` for black-and-white output |
| `.withRotation`, `.withWatermark`, `.withDialect`, `.withParameter` | Unchanged, after `to*` |
| `from(format, body)` and `to(target)` with format-prefixed options | Unchanged names, but source options such as `withPdfPage` and `withZplCommandsToIgnore` come before `to(target)` |
| `ConversionSourceBuilder`, `ConversionTargetBuilder` | `SourceStage` and `TargetStage`, and their subclasses, such as `PdfSourceBuilder` and `ZplTargetBuilder` |
| A `422` rejected as `LabelZoomError` | `UnprocessableError` |
| No print calls | `toPrinter`, `fromTemplate`, `client.print`, `client.printTemplate`, `client.jobs.get`, and `client.getPrintQuota` |

The rest of the object form, `client.convert({ from, to, body, options })`, is unchanged.

## Contributing

To build from source:

```sh
git clone https://github.com/labelzoom/labelzoom-sdk.git
cd labelzoom-sdk/node
npm ci
npm run build       # dual ESM/CJS and .d.ts into dist/
```

To check a change:

```sh
npm test            # offline; no key, no network
npm run typecheck   # npm run lint runs the same check
```

The test suite runs the shared [conformance fixtures](../conformance/) that every LabelZoom SDK is
checked against, and asserts that it ran all of them. Each SDK is versioned on its own, and the
contract carries its own version in `conformance/spec.json`. See
[docs/CONFORMANCE.md](../docs/CONFORMANCE.md).

## License

MIT. See [LICENSE](../LICENSE).
