![LabelZoom Logo](https://raw.githubusercontent.com/labelzoom/labelzoom-sdk/main/docs/LabelZoom_Logo_f_400px.png)

# LabelZoom PHP SDK

The LabelZoom PHP SDK is the official PHP client for the [LabelZoom API](https://api.labelzoom.com).
Use it to convert barcode labels between printer languages, PDF, images, and LabelZoom XML and JSON,
to read NiceLabel, BarTender, and DYMO label designs, and to print labels through LabelZoom Cloud
Print.

## What the SDK handles

- Retries for `429`, `5xx`, and transport failures, with backoff that honors `Retry-After`.
- Typed exceptions that carry the HTTP status, the server's message, the raw body, the error `code`,
  and the `X-LZ-Request-Id` request ID.
- Binary-safe results: `getBytes()` returns the document exactly as the server sent it.
- Idempotent printing: every print carries an `Idempotency-Key`, so a retry can't print twice.
- PSR-18 and PSR-17 throughout, so it works with Guzzle, Symfony HttpClient, or the bundled cURL
  client with no adapter.
- Works without an API key, on the watermarked free tier. See
  [Rate limits and plans](https://docs.labelzoom.com/reference/rate-limits-and-plans/).

## Install

The SDK requires PHP 8.1 or later with the `json` and `mbstring` extensions. The bundled HTTP client
also needs the `curl` extension, which you don't need if you pass your own PSR-18 client.

Install the [`labelzoom/sdk`](https://packagist.org/packages/labelzoom/sdk) package with Composer:

```sh
composer require labelzoom/sdk
```

## Convert a label

A conversion is a pipeline: choose the source with a `from*` method, set source options, choose the
target with a `to*` method, set target options, and then call `execute()`.

The following program converts page 0 of a PDF to ZPL for a 203 dpi printer:

```php
<?php

require __DIR__ . '/vendor/autoload.php';

use LabelZoom\Sdk\LabelZoomClient;

$client = new LabelZoomClient();               // reads LABELZOOM_API_KEY

$result = $client->convert()
    ->fromPdf(file_get_contents('shipping-label.pdf'))
        ->withPage(0)                          // source option: 0-based
        ->withLabelSize(4, 6)                  // source option: inches
    ->toZpl()
        ->withDpi(203)                         // target option: the printer's resolution
    ->execute();

$result->save('shipping-label.zpl');
```

Source options change how the document is read: its label size, which page or label to read, the
data that fills its fields, and its resolution. They go after `from*` and before `to*`. Target
options change what's written: the output resolution, rotation, watermark, and format-specific
settings such as ZPL image compression. They go after `to*` and before `execute()`. Each stage, and
each named format, offers only the options that act on it, so `fromZpl($zpl)->withPage(0)` fails
static analysis. See [Options by stage](#options-by-stage).

### Set the API key

| Construction | Credential |
|---|---|
| `new LabelZoomClient()` | The `LABELZOOM_API_KEY` environment variable, or anonymous if it isn't set |
| `new LabelZoomClient('API_KEY')` | The key you pass: an `lz_live_` or `lz_test_` key, or a JWT |
| `new LabelZoomClient(null)` or `new LabelZoomClient('')` | Anonymous; the environment variable isn't read |

Without a key, requests use the free tier: watermarked output, one label per request (the first, or
the one `withLabelIndex` selects), and no multi-page, JSON-target, dialect, or image-to-image
conversion. Passing `null` or `''` forces anonymous mode, so a configuration value that resolves to
empty can't pick up an unrelated key from the environment. `isAuthenticated()` reports whether the
client resolved a key.

The client is stateless once constructed. Create one for the application and reuse it, not one per
request.

| Constructor argument | Default | What it does |
|---|---|---|
| `baseUrl` | `https://api.labelzoom.com` | The API base URL. A path prefix is kept, so a reverse proxy works. |
| `maxRetries` | `2` | Retries after the first attempt. `0` turns retries off. |
| `timeout` | `30.0` | Per-request timeout in seconds, for the bundled cURL client. |
| `userAgentSuffix` | none | Text appended to the SDK's `User-Agent`. |

### Read the result

`getBytes()` is the document exactly as the server sent it. PDF and image targets are binary, and
every printer-language target can carry binary graphics, so write output as bytes.

```php
$result->getBytes();        // the document, exactly as sent
$result->getText();         // decoded with the response charset; for ZPL, XML, and JSON
$result->getContentType();
$result->getRequestId();    // X-LZ-Request-Id; quote it to support
$result->save('out.pdf');   // writes getBytes() to a file
```

### Fill variable fields

`withData()` takes a list of records, or a single record. Each record produces one label.

```php
$result = $client->convert()
    ->fromZpl($template)
        ->withData([
            ['sku' => 'A-1', 'qty' => 12],
            ['sku' => 'B-2', 'qty' => 4],
        ])
    ->toPdf()
    ->execute();                               // a 2-page PDF
```

### Move a label to a printer of another density

To move a label made for a 300 dpi printer to a 203 dpi printer at the same physical size, set the
resolution on both stages:

```php
$result = $client->convert()
    ->fromZpl($zplFor300Dpi)
        ->withDpi(300)                         // what the job was made for
    ->toZpl()
        ->withDpi(203)                         // the printer it's going to
    ->execute();
```

### Split a sheet into labels

The following example cuts a Letter sheet of four labels into a grid, and lays them out two across
for a 2-up roll:

```php
use LabelZoom\Sdk\LayoutOptions;
use LabelZoom\Sdk\SplitOptions;

$result = $client->convert()
    ->fromPdf($sheet)
        ->withSplit(fn (SplitOptions $s) => $s->rows(2)->columns(2)->skipBlank())
    ->toZpl()
        ->withLayout(fn (LayoutOptions $l) => $l->across(2)->gap(0.125))   // inches
    ->execute();
```

## Print a label

To print, use the same chain with a printer as the target: call `toPrinter()` where you would call
`toZpl()`. The printer's configuration sets its language, and LabelZoom converts the document to
it, so a PDF prints on a ZPL printer. Printing needs an API key with the `print` scope.

```php
$job = $client->convert()
    ->fromPdf($pdf)
        ->withPage(0)                          // the same source options as any conversion
    ->toPrinter($printerId)
        ->withCopies(2)
        ->withIdempotencyKey($orderNumber)
    ->execute();                               // PrintJob

$batch = $client->convert()
    ->fromTemplate($templateId)
        ->withData([
            ['orderNumber' => 'SO-1001', 'sku' => 'A-100'],
            ['orderNumber' => 'SO-1002', 'sku' => 'B-200'],
        ])
        ->withValidate()
    ->toPrinter($printerId)
        ->withIdempotencyKey($batchId)
    ->execute();                               // PrintBatch

$details = $client->jobs()->get($job->getJobId());
echo $details->getStatus();                    // queued, dispatched, printing, completed, or failed

$quota = $client->getPrintQuota();
echo $quota->getJobsToday()->getRemaining();
```

| Call | Returns |
|---|---|
| `toPrinter($printerId)->execute()` | `PrintJob`: `getJobId()`, `getStatus()`, `getCopies()`, `isIdempotent()` |
| `fromTemplate($templateId)->toPrinter($printerId)->execute()` | `PrintBatch`: `getJobs()`, `getRemaining()`, `isComplete()`, `getMessage()` |
| `jobs()->get($jobId)` | `PrintJobDetails`: `getStatus()`, `getError()`, `getRetryCount()`, `getPrinterId()`, and the created, dispatched, completed, and retried dates |
| `getPrintQuota()` | `PrintQuota`: `getPlan()`, `getAgents()`, `getPrinters()`, `getJobsToday()`, `getLimits()` |

- Every source prints except a URL and base64 text. `toPrinter()` throws a `ValidationException`
  for a source from `fromUrl()` or `fromBase64Text()`, because print reads the document's own bytes
  and doesn't fetch.
- The printer target has one target option, `withRotation()`, which turns the finished label. The
  other target options depend on the output language, which the printer sets.
- `fromTemplate($templateId)` prints a template published from LabelZoom Studio or the web
  designer. It takes `withData()` and `withValidate()` instead of source options. `withValidate()`
  checks every record for missing merge fields before anything prints, and a missing field fails the
  request with an `UnprocessableException`.
- `withCopies($n)` sends the whole job `$n` times, from 1 to 9999. It multiplies a quantity in the
  document: `^PQ3` with 5 copies prints 15 labels.
- Every print carries an `Idempotency-Key`. Set your own with `withIdempotencyKey()`, such as an
  order number, to make a repeated call return the original job instead of printing again. Without
  one, the SDK generates a key for each `execute()` and reuses it on that call's retries. Keys are
  scoped to the account, so a deliberate reprint needs a new key.
- A template batch that runs out of time returns a nonzero `getRemaining()`: the number of records
  not yet submitted. Send the same request with the same key to submit them.
- `getStatus()` is a string, and new statuses can be added. Don't fail on one you don't recognize.
- The daily job limit throws a `PrintQuotaExceededException`, which is never retried, because the
  limit resets at 00:00 UTC. Check `getPrintQuota()` before a large run.

## Formats

**Sources:** `zpl` `epl` `ipl` `tspl` `dpl` `sbpl` `esclabel` `fingerprint` `ezpl` `cpcl` `tpcl`
`nlbl` `lbl` `btw` `dymo` `xml` `json` `pdf` `png` `bmp` `gif` `jpg` `jpeg` `url`

**Targets:** `zpl` `epl` `ipl` `tspl` `dpl` `sbpl` `cpcl` `xml` `json` `pdf` `png` `bmp` `gif`
`jpeg`

- ESC/Label, Fingerprint, EZPL, TPCL, NiceLabel (`nlbl`, `lbl`), BarTender (`btw`), and DYMO are
  source-only. Send NiceLabel and BarTender files as bytes.
- `jpg` and `url` are source-only: `jpg` normalizes to `jpeg`, and `url` is a fetch instruction
  rather than a format.
- `SourceFormat` and `TargetFormat` are separate enums, so there is no `TargetFormat::Url`. Naming
  one is a PHPStan error, not a runtime `404`:

  ```php
  $client->convert()->fromUrl($url)->toZpl();     // fine
  $client->convert()->fromZpl($zpl)->to(TargetFormat::Url);
  //                                  ^^^^^^^^^^^^^^^^^^^ Access to undefined constant
  ```

- EPL, IPL, TSPL, DPL, SBPL, and CPCL output is `text/plain` with every label concatenated. EPL
  (`GW`), TSPL (`BITMAP`), IPL (bitmap columns framed in `STX` and `ETX`), and SBPL (inline graphics)
  can carry raw binary, so read `getBytes()` rather than `getText()` whenever a label might carry
  graphics. CPCL writes graphics as hex, so its output is plain text.
- ZPL, the other printer-language targets, and PDF contain every label; PDF has one page per label.
  XML, JSON, and the image targets return the first label only. JSON output needs a paid plan.

### Read source bytes from a file, a stream, or a URL

```php
use LabelZoom\Sdk\SourceFormat;

$client->convert()->fromFile(SourceFormat::Pdf, 'invoice.pdf')->toZpl()->execute();
$client->convert()->fromStream(SourceFormat::Png, $handle)->toZpl()->execute();
$client->convert()->fromBase64Text(SourceFormat::Pdf, $base64)->toZpl()->execute();
$client->convert()->fromUrl($url)->toZpl()->execute();
```

`fromUrl()` has the server fetch the URL you supply and convert what it finds. If the URL comes from
untrusted input, validate it first.

## Options by stage

Every `with*` call sets one field of the API's `params` object. The SDK sends only the options you
set and never fills in a client-side default, so the server's default applies to anything you leave
out.

### Source options

Set these after `from*` and before `to*`.

| Method | Available on | What it does |
|---|---|---|
| `withLabelSize(float $widthInches, float $heightInches)` | every source | The label size in **inches**, not dots. Replaces the size the source declares. |
| `withLabelIndex(int)` | every source | Converts only this label, **0-based**, on every plan. For an image or PDF, it counts labels after any split. |
| `withData(array)` | every source | The records that fill the label's variable fields. Each record produces one label. |
| `withDpi(int)` | `fromZpl`, `fromEscLabel`, the other printer-language sources, and image sources | The resolution the job or image was made for. The server default is 203. |
| `withPage(int)` | `fromPdf` | The page to convert, **0-based**. Omit it to convert every page. |
| `withConversionMode(PdfConversionMode)` | `fromPdf` | `Image`, the default, or `Native`. |
| `withSplit(fn (SplitOptions $s) => …)` | `fromPdf`, `fromPng`, `fromBmp`, `fromGif`, `fromJpeg` | Cuts each page into a grid of labels, for a printer-language target in `Image` mode: `rows`, `columns`, `labelSize`, `margins`, `gaps` (all in **inches**), and `skipBlank`. |
| `withRaster(fn (RasterOptions $r) => …)` | `fromPdf`, `fromPng`, `fromBmp`, `fromGif`, `fromJpeg` | How the picture is rasterized: `scaling` (percent), `position` (output dots from the label's top-left corner; a negative offset crops), `darkness` (0 to 100, default 70), and `colorMode` (`Grayscale`, the default, dithers). |
| `withCommandsToIgnore(array)` | `fromZpl`, `fromEscLabel` | ZPL commands to skip, such as `['^PQ']`. |

NiceLabel, BarTender, DYMO, LabelZoom XML, and LabelZoom JSON sources take only the options that
every source takes.

### Target options

Set these after `to*` and before `execute()`.

| Method | Available on | What it does |
|---|---|---|
| `withDpi(int)` | every target except `toPdf` | The output resolution. The server default is the source's resolution, or else 203. |
| `withRotation(int)` | every target, and `toPrinter` | Rotation in degrees clockwise. Must be a multiple of 90; anything else throws a `ValidationException` before any request. |
| `withWatermark(bool)` | every target | Requests a watermark. The free tier is watermarked regardless. |
| `withDialect(string)` | every target | A printer dialect, such as `'moca'`. Needs a paid plan. |
| `withParameter(string, mixed)` | every target | Sets a `params` field the SDK doesn't model, sent as given. |
| `withRawQueryParameter(string, string)` | every target | Adds a query-string parameter alongside `params`. |
| `withImageCompression(ZplImageCompression)` | `toZpl` | `Z64`, the default, or `CompressedHex`. |
| `withCollapseCopies(bool)` | `toZpl` | For an image or PDF source, turns each run of identical labels into one label with a `^PQ` count. |
| `withLayout(fn (LayoutOptions $l) => …)` | `toZpl` | For an image or PDF source in `Image` mode, places labels side by side in each row: `across`, `gap` (**inches**), and `fill` (`Sequential` or `Repeat`). |
| `withColorMode(ColorMode)`, `withDarkness(int)` | `toPng`, `toBmp`, `toGif`, `toJpeg` | `Bw` renders black and white at the darkness threshold. Any other mode renders in color. |
| `withCopies(int)`, `withIdempotencyKey(string)` | `toPrinter` | See [Print a label](#print-a-label). |

### Formats chosen at run time

`from()`, `fromFile()`, `fromStream()`, `fromBase64Text()`, and `fromUrl()` return a
`DynamicSourceBuilder`, and `to(TargetFormat)` returns a `DynamicTargetBuilder`. Static analysis
can't know the format there, so these builders offer every option, named with the format it applies
to: `withPdfPage`, `withPdfConversionMode`, `withZplCommandsToIgnore`, `withZplImageCompression`, and
`withZplCollapseCopies`, plus `withDpi`, `withSplit`, `withRaster`, `withLayout`, `withColorMode`, and
`withDarkness`. The server ignores an option that doesn't apply to the format.

## Errors

Every non-2xx response throws a subclass of `LabelZoomException`, which extends `RuntimeException`.

| Exception | Status |
|---|---|
| `BadRequestException` | 400 |
| `UnauthorizedException` | 401 |
| `ForbiddenException` | 403. `isPaidFeature()` is true when the request needs a paid plan. |
| `NotFoundException` | 404 |
| `PayloadTooLargeException` | 413 |
| `UnprocessableException` | 422 |
| `RateLimitedException` | 429, after retries. `getRetryAfterSeconds()` returns `Retry-After`. |
| `PrintQuotaExceededException` | 429 for the daily print job limit. Extends `RateLimitedException`. |
| `ServerErrorException` | 5xx, after retries |
| `LabelZoomException` | any other status |

Every `LabelZoomException` carries the following:

| Method | Returns |
|---|---|
| `getStatus()` | The HTTP status code. |
| `getMessage()` | The server's message, truncated to 512 characters. |
| `getRawBody()` | The full response body. |
| `getErrorCode()` | The error's stable code, such as `agent_update_required`, or `null`. Branch on this rather than on the message. `getCode()` is PHP's own integer code. |
| `getRequestId()` | The `X-LZ-Request-Id` header. Quote it to LabelZoom support. |

Two exceptions don't extend `LabelZoomException`, because there is no response to describe:

- `ValidationException` extends `InvalidArgumentException`. The SDK rejected an argument, such as a
  rotation that isn't a multiple of 90, before sending any request. `getParameter()` names the
  argument. It's never retried.
- `TransportException` extends `RuntimeException`. No response arrived: a DNS failure, a refused
  connection, a TLS error, or a timeout. It's thrown after retries run out.

```php
use LabelZoom\Sdk\Exception\ForbiddenException;
use LabelZoom\Sdk\Exception\LabelZoomException;

try {
    $client->convert()->fromZpl($zpl)->toJson()->execute();
} catch (ForbiddenException $e) {
    if ($e->isPaidFeature()) {
        echo $e->getMessage() . ' (request ' . $e->getRequestId() . ')';
    }
} catch (LabelZoomException $e) {
    echo $e->getStatus() . ' ' . ($e->getErrorCode() ?? '') . ': ' . $e->getMessage();
}
```

## Retries

The SDK retries `429` and `5xx` responses and transport failures. It makes 3 attempts in total, with
backoff of 1, 2, and 4 seconds and full jitter, and waits longer when a `Retry-After` header asks for
more. The daily print job limit is the exception: its `429` throws at once. Other `4xx` responses
throw immediately. A print's retries all send the same `Idempotency-Key`, so a retry can't print
twice.

To change the number of retries, pass `maxRetries`. `0` turns retries off:

```php
$client = new LabelZoomClient(maxRetries: 0, timeout: 30.0);
```

## Bring your own HTTP client

The bundled `CurlHttpClient` is the default. Any PSR-18 client works:

```php
$client = new LabelZoomClient(
    httpClient: new \GuzzleHttp\Client(['connect_timeout' => 2]),
);
```

To use your own PSR-17 factories instead of the bundled `nyholm/psr7`, pass them too:

```php
$psr17 = new \GuzzleHttp\Psr7\HttpFactory();
$client = new LabelZoomClient(
    httpClient: $guzzle,
    requestFactory: $psr17,
    streamFactory: $psr17,
);
```

## Test your own code

The HTTP client and the retry delay are both injectable, so you can test retry handling without
waiting:

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

## Migrate from 1.x

In 1.x, every option was set after `to*`. In 2.0, source options move before `to*`, and the typed
builders offer only the options that apply to their format.

| 1.x | 2.0 |
|---|---|
| `->fromPdf($pdf)->toZpl()->withPdfPage(0)` | `->fromPdf($pdf)->withPage(0)->toZpl()` |
| `->withPdfConversionMode($m)` | `->fromPdf($pdf)->withConversionMode($m)` |
| `->withZplCommandsToIgnore(['^PQ'])` | `->fromZpl($zpl)->withCommandsToIgnore(['^PQ'])` |
| `->withLabelSize($w, $h)`, `->withData($records)` | The same methods, before `to*` |
| `->withZplImageCompression($c)` | `->toZpl()->withImageCompression($c)` |
| `->withDpi($d)`, which set both resolutions | `->withDpi($d)` after `to*` for the output resolution. For a printer-language or image source made for `$d`, also call `->withDpi($d)` before `to*`. |
| `->withScaling($p)`, `->withPosition($x, $y)` | `->fromPdf($pdf)->withRaster(fn (RasterOptions $r) => $r->scaling($p)->position($x, $y))`, or the same on `fromPng()` and the other image sources |
| `->withDarkness($d)`, `->withColorMode($m)` | In `withRaster()` for an image or PDF source. On `toPng()` and the other image targets for black-and-white output. |
| `->withRotation()`, `->withWatermark()`, `->withDialect()`, `->withParameter()`, `->withRawQueryParameter()` | Unchanged, after `to*` |
| `->from($format, $body)->to($target)->withPdfPage(0)` | `->from($format, $body)->withPdfPage(0)->to($target)`. The run-time builders keep the format prefix on each option. |
| `ConversionSourceBuilder`, `ConversionTargetBuilder` | `SourceStage` and `TargetStage` subclasses, such as `PdfSourceBuilder` and `ZplTargetBuilder` |
| No print methods | `toPrinter()`, `fromTemplate()`, `jobs()->get()`, and `getPrintQuota()`. See [Print a label](#print-a-label). |

## Contributing

To build from source:

```sh
git clone https://github.com/labelzoom/labelzoom-sdk.git
cd labelzoom-sdk/php
composer install
```

Then run the checks:

```sh
composer test        # PHPUnit; offline, with no key and no network
composer analyze     # PHPStan: src at level 9, tests at level 5
composer lint        # php-cs-fixer
```

The test suite runs the shared
[conformance fixtures](https://github.com/labelzoom/labelzoom-sdk/tree/main/conformance) that every
LabelZoom SDK is checked against, and asserts that it ran all of them: PHP declares no skips. That
includes the two `typecheck/*` cases, which PHPStan enforces. Each SDK is versioned independently,
and the contract carries its own version in `conformance/spec.json`. See
[docs/CONFORMANCE.md](../docs/CONFORMANCE.md).

## License

MIT. See [LICENSE](https://github.com/labelzoom/labelzoom-sdk/blob/main/LICENSE).
