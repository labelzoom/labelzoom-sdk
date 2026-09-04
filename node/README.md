![LabelZoom Logo](../docs/LabelZoom_Logo_f_400px.png)

# LabelZoom Node.js / TypeScript SDK

Official Node client for the [LabelZoom API](https://api.labelzoom.com). Converts barcode labels
between ZPL, EPL, TSPL, DPL, PDF, LabelZoom XML/JSON, and raster images.

Node 20+. Ships dual ESM/CJS builds and TypeScript declarations. No runtime dependencies — it
uses the platform `fetch`.

## Install

```sh
npm install @labelzoom/sdk
```

Published from CI with [npm provenance](https://docs.npmjs.com/generating-provenance-statements),
so every release is cryptographically linked to the commit and workflow that built it.

> **Stable at `1.0.0`.** The public API is covered by a shared conformance suite that
> every language SDK runs against the same fixtures, and it is versioned independently of
> the other SDKs — the contract carries its own version in `conformance/spec.json`.

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
  .toPng()
  .withDpi(300)
  .withLabelSize(4, 6)
  .execute();
```

Both forms do the same thing. The object form is what most TypeScript users reach for; the chain
exists for parity with the .NET, Java, and PHP SDKs.

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
  .toPdf()
  .withData({ name: 'ACME Corp', sku: '12345' }, { name: 'Globex', sku: '67890' })
  .execute();                                    // a 2-page PDF
```

## Formats

```ts
type SourceFormat = 'zpl' | 'epl' | 'tspl' | 'dpl' | 'xml' | 'json'
                  | 'pdf' | 'png' | 'bmp' | 'gif' | 'jpeg' | 'jpg' | 'url';

type TargetFormat = 'zpl' | 'epl' | 'tspl' | 'dpl' | 'xml' | 'json'
                  | 'pdf' | 'png' | 'bmp' | 'gif' | 'jpeg';
```

`jpg` and `url` are **source-only** — `jpg` normalizes to `jpeg`, and `url` is a fetch instruction
rather than a format — and the type system says so:

```ts
await client.convert({ from: 'pdf', to: 'url', body });
//                                      ~~~~~ Type '"url"' is not assignable to 'TargetFormat'
```

A compile error, not a runtime 404.

`epl`, `tspl` and `dpl` are targets as well as sources — ``{ from: 'pdf', to: 'epl' }`` is a real conversion. Their
output is `text/plain` with every label concatenated, but EPL's `GW` and TSPL's `BITMAP` commands
inline raw binary: read `result.bytes` rather than `result.text` whenever a label might carry graphics.

`from: 'url'` has the *server* fetch a URL you supply and convert what it finds. Validate the URL
first if it came from untrusted input.

## Options

| Option | Notes |
|---|---|
| `sourceDpi` | how the source's positions are interpreted; not applicable to PDF sources |
| `targetDpi` | resolution the output is authored at; server default 203 |
| `dpi` | legacy alias applied to whichever side(s) the path supports; still accepted |
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
| `data` | one label per record |

Only options you actually set are sent. The SDK never fills in a client-side default, so a change
to a server default reaches you without an SDK upgrade. Unknown keys are ignored server-side, so
the index signature on `ConversionOptions` is a safe forward-compatibility escape hatch.

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
honouring a longer `Retry-After`. Other 4xx responses reject immediately — a malformed request
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
