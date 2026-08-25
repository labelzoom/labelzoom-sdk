![LabelZoom Logo](https://raw.githubusercontent.com/labelzoom/labelzoom-sdk/main/docs/LabelZoom_Logo_f_400px.png)

# LabelZoom PHP SDK

Official PHP client for the [LabelZoom API](https://api.labelzoom.com). Converts barcode labels
between ZPL, EPL, TSPL, DPL, PDF, LabelZoom XML/JSON, and raster images.

PHP 8.1+. PSR-18 / PSR-17 throughout, so it drops into Guzzle, Symfony HttpClient, or the bundled
cURL client with no adapter.

## Install

```sh
composer require labelzoom/sdk
```

> **Stable at `1.0.0`.** The public API is covered by a shared conformance suite that
> every language SDK runs against the same fixtures, and it is versioned independently of
> the other SDKs — the contract carries its own version in `conformance/spec.json`.

<details>
<summary>Build from source</summary>

```sh
git clone https://github.com/labelzoom/labelzoom-sdk.git
cd labelzoom-sdk/php
composer install
```
</details>

## Quick start

**An API key is optional.** Without one you get the free tier — watermarked output, first label
only, a 1 MB request cap, and no multi-page, JSON-target, or image-to-image conversion.

```php
use LabelZoom\Sdk\LabelZoomClient;

$client = new LabelZoomClient();            // anonymous; this works

$result = $client->convert()
    ->fromZpl('^XA^FO20,20^A0N,28^FDHello^FS^XZ')
    ->toPng()
    ->withDpi(300)
    ->withLabelSize(widthInches: 4, heightInches: 6)
    ->execute();

$result->save('label.png');
```

With a key:

```php
$client = new LabelZoomClient('lz_live_...');
```

Omitting the argument entirely reads `LABELZOOM_API_KEY` from the environment. Passing `null` or
`''` explicitly forces anonymous mode and suppresses that fallback — the two are deliberately
different, so a config value that resolves to empty cannot silently pick up an unrelated key.

The client is stateless once constructed and safe to keep for the life of the process. Create one
per application, not one per request.

## Formats

**Sources (12, plus `url`):** `zpl` `epl` `tspl` `dpl` `xml` `json` `pdf` `png` `bmp` `gif` `jpg`
`jpeg`

**Targets (11):** `zpl` `epl` `tspl` `dpl` `xml` `json` `pdf` `png` `bmp` `gif` `jpeg`

`jpg` and `url` are **source-only** — `jpg` normalizes to `jpeg`, and `url` is a fetch instruction
rather than a format. `SourceFormat` and `TargetFormat` are separate enums, so there is no
`TargetFormat::Url` to write:

```php
$client->convert()->fromUrl($url)->toZpl();     // fine
$client->convert()->fromZpl($zpl)->to(TargetFormat::Url);
//                                  ^^^^^^^^^^^^^^^^^^^ Access to undefined constant
```

A PHPStan error, not a runtime 404. The two `typecheck/*` conformance cases assert exactly this,
and PHP is the only dynamically executed SDK that runs them rather than declaring them skipped.

`epl`, `tspl` and `dpl` are targets as well as sources — ``fromPdf($bytes)->toEpl()`` is a real conversion. Their
output is `text/plain` with every label concatenated, but EPL's `GW` and TSPL's `BITMAP` commands
inline raw binary: read `$result->getBytes()` rather than `$result->getText()` whenever a label might carry graphics.

Reading source bytes from wherever they live:

```php
$client->convert()->fromFile(SourceFormat::Pdf, 'invoice.pdf')->toZpl()->execute();
$client->convert()->fromStream(SourceFormat::Png, $handle)->toZpl()->execute();
$client->convert()->fromBase64Text(SourceFormat::Pdf, $base64)->toZpl()->execute();
```

`fromUrl()` has the *server* fetch a URL you supply and convert what it finds. Validate the URL
first if it came from untrusted input.

## Options

Every `with*` call maps to one field of the API's `?params=` object.

| Method | Notes |
|---|---|
| `withDpi(int)` | server default 203 |
| `withRotation(int)` | must be a multiple of 90; rejected locally otherwise |
| `withScaling(float)` | percent, server default 100 |
| `withColorMode(ColorMode)` | `Bw`, `Grayscale` (default), `Color` |
| `withDarkness(int)` | 0–100, server default 70 |
| `withPosition(int $x, int $y)` | pixel offset of the extracted region |
| `withWatermark(bool)` | forced on for the free tier regardless |
| `withDialect(string)` | e.g. `'moca'`; **paid** |
| `withLabelSize(float, float)` | **inches**, not dots |
| `withPdfConversionMode(PdfConversionMode)` | `Image` (default) or `Native` |
| `withPdfPage(int)` | **0-based**; omit to convert every page |
| `withZplCommandsToIgnore(array)` | e.g. `['^PQ']` |
| `withZplImageCompression(ZplImageCompression)` | `Z64` (default) or `CompressedHex` |
| `withData(array)` | one label per record |
| `withParameter(string, mixed)` | anything not modeled yet; sent as-is |

Only options you actually set are sent. The SDK never fills in a client-side default, so a change
to a server default reaches you without an SDK upgrade.

`withData()` takes either a list of records or a single record, which is wrapped rather than
rejected. **Each record produces one label.**

```php
$result = $client->convert()
    ->fromZpl($template)
    ->toPdf()
    ->withData([
        ['sku' => 'A-1', 'qty' => 12],
        ['sku' => 'B-2', 'qty' => 4],
    ])
    ->execute();                       // a 2-page PDF
```

## Results

`getBytes()` is authoritative — five of the eleven targets are binary, and the `epl`/`tspl` text
targets can inline binary of their own.

```php
$result->getBytes();        // the document, exactly as sent
$result->getText();         // decoded using the response charset, for zpl/xml/json
$result->getContentType();
$result->getRequestId();    // X-LZ-Request-Id — quote it to support
$result->save('out.pdf');
```

## Errors

Every non-2xx throws a typed exception carrying the server's own message, the raw body, and the
`X-LZ-Request-Id` support handle. The body is never discarded, and `getMessage()` is truncated to
512 characters while `getRawBody()` is not.

```php
use LabelZoom\Sdk\Exception\ForbiddenException;
use LabelZoom\Sdk\Exception\LabelZoomException;

try {
    $client->convert()->fromZpl($zpl)->toJson()->execute();
} catch (ForbiddenException $e) {
    if ($e->isPaidFeature()) {
        // "JSON export is a paid feature" — the most common free-tier failure.
        echo $e->getMessage() . ' (request ' . $e->getRequestId() . ')';
    }
} catch (LabelZoomException $e) {
    echo $e->getStatus() . ': ' . $e->getMessage();
}
```

`BadRequestException`, `UnauthorizedException`, `ForbiddenException`, `NotFoundException`,
`PayloadTooLargeException`, `RateLimitedException` and `ServerErrorException` all extend
`LabelZoomException`, which extends `RuntimeException`.

Two exceptions deliberately do **not** extend it, because neither has a status, a body or a
request id to carry, and a handler written for server failures should not swallow them:

- `ValidationException` (`extends InvalidArgumentException`) — the calling code is wrong, and no
  HTTP request was made.
- `TransportException` (`extends RuntimeException`) — no response was ever produced. Thrown only
  once retries are exhausted.

## Retries

429, 5xx and transport failures are retried automatically: 3 attempts, 1s/2s/4s with full jitter,
honouring a longer `Retry-After` on any retryable status. Other 4xx responses throw immediately —
a malformed request will not become valid on a second attempt.

```php
$client = new LabelZoomClient(maxRetries: 0, timeout: 30.0);
```

## Bring your own HTTP client

The bundled `CurlHttpClient` is a default, not a dependency. Any PSR-18 client works:

```php
$client = new LabelZoomClient(
    httpClient: new \GuzzleHttp\Client(['connect_timeout' => 2]),
);
```

Pass PSR-17 factories too if you would rather not use the bundled `nyholm/psr7`:

```php
$psr17 = new \GuzzleHttp\Psr7\HttpFactory();
$client = new LabelZoomClient(
    httpClient: $guzzle,
    requestFactory: $psr17,
    streamFactory: $psr17,
);
```

## Testing your own code

The transport and the retry delay are both injectable, so retry logic can be tested without
spending the wall-clock time:

```php
use LabelZoom\Sdk\LabelZoomClient;
use LabelZoom\Sdk\Sleeper;

$sleeper = new class implements Sleeper {
    /** @var list<float> */
    public array $slept = [];

    public function sleep(float $seconds): void
    {
        $this->slept[] = $seconds;
    }
};

$client = new LabelZoomClient(
    apiKey: null,
    httpClient: $yourPsr18Stub,
    sleeper: $sleeper,
    useJitter: false,          // deterministic backoff, so assertions can be exact
);

// ... provoke a retry, then:
assert($sleeper->slept === [1.0, 2.0]);
```

## Development

```sh
composer install
composer test        # offline; no key, no network
composer analyse     # PHPStan: src at level 9, tests at 5
composer lint        # php-cs-fixer
```

The test suite runs the shared
[conformance fixtures](https://github.com/labelzoom/labelzoom-sdk/tree/main/conformance) that
every LabelZoom SDK is checked against, and asserts it ran **all** of them — PHP declares no
skips. See
[docs/CONFORMANCE.md](https://github.com/labelzoom/labelzoom-sdk/blob/main/docs/CONFORMANCE.md).

## License

MIT — see [LICENSE](https://github.com/labelzoom/labelzoom-sdk/blob/main/LICENSE).
