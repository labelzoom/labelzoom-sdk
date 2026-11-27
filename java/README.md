![LabelZoom Logo](../docs/LabelZoom_Logo_f_400px.png)

# LabelZoom Java SDK

The official Java client for the [LabelZoom API](https://api.labelzoom.com). Use it to convert
barcode labels between ZPL, EPL, IPL, TSPL, DPL, SBPL, CPCL, PDF, LabelZoom XML and JSON, and raster
images, to read ESC/Label, Fingerprint, EZPL, TPCL, NiceLabel, BarTender, and DYMO files, and to print
labels on LabelZoom Cloud Print printers.

## What the SDK handles

- Retries for `429`, `5xx`, and transport failures, with backoff that honors `Retry-After`.
- Typed exceptions that carry the HTTP status, the server's message and error `code`, the raw body,
  and the `X-LZ-Request-Id` support handle.
- Binary-safe results: `ConversionResult.bytes()` returns the document exactly as the server sent it.
- Idempotent printing: every print request carries an `Idempotency-Key`, so a retried print doesn't
  print twice.
- Calls without an API key, on the watermarked free tier. See
  [Rate limits and plans](https://docs.labelzoom.com/reference/rate-limits-and-plans/).
- Local validation of option values, before any request is sent.

## Install

The SDK requires Java 17 or later, or Android 8.0 (API level 26) or later. It has no runtime
dependencies.

[![Maven Central](https://img.shields.io/maven-central/v/com.labelzoom/labelzoom-sdk)](https://central.sonatype.com/artifact/com.labelzoom/labelzoom-sdk)
Replace `VERSION` with the version on the badge.

Gradle:

```gradle
implementation 'com.labelzoom:labelzoom-sdk:VERSION'
```

Maven:

```xml
<dependency>
    <groupId>com.labelzoom</groupId>
    <artifactId>labelzoom-sdk</artifactId>
    <version>VERSION</version>
</dependency>
```

### Create a client

An API key is optional. Without one, the client uses the free tier: output is watermarked, one label
converts per request (the first, or the one `withLabelIndex` selects), and multi-page, JSON-target,
dialect, and image-to-image conversions are not available.

```java
LabelZoomClient.builder().build();              // reads LABELZOOM_API_KEY
LabelZoomClient.builder().apiKey("API_KEY").build();
LabelZoomClient.builder().apiKey("").build();   // forces anonymous, ignoring LABELZOOM_API_KEY
```

The client is thread-safe. Create one for your application and reuse it.

| Builder method | What it sets |
|---|---|
| `apiKey(String)` | The bearer credential: an `lz_live_` or `lz_test_` key, or a JWT. Without this call, the client reads `LABELZOOM_API_KEY`. `null` or `""` forces anonymous mode. |
| `baseUrl(String)` | The API base URL. The default is `https://api.labelzoom.com`. A path prefix is kept, so a reverse proxy works. |
| `timeout(Duration)` | The per-request timeout of the default transport. |
| `maxRetries(int)` | Retries after the first attempt. The default is `2`; `0` turns retries off. |
| `userAgentSuffix(String)` | Text appended to the SDK's `User-Agent`, to identify your application. |
| `transport(HttpTransport)` | Replaces the HTTP layer. See [Android](#android) and [Test your own code](#test-your-own-code). |

## Convert a label

A conversion is a pipeline: choose the source with a `from*` method, set source options, choose the
target with a `to*` method, set target options, and call `execute()`.

This program converts the first page of a PDF to ZPL for a 203 dpi printer:

```java
import com.labelzoom.sdk.ConversionResult;
import com.labelzoom.sdk.LabelZoomClient;
import java.nio.file.Files;
import java.nio.file.Path;

public class PdfToZpl {
    public static void main(String[] args) throws Exception {
        try (LabelZoomClient client = LabelZoomClient.builder().build()) {
            ConversionResult result = client.convert()
                    .fromPdf(Files.readAllBytes(Path.of("shipping-label.pdf")))
                        .withPage(0)            // 0-based; omit to convert every page
                        .withLabelSize(4f, 6f)  // inches
                    .toZpl()
                        .withDpi(203)           // the printer's resolution
                    .execute();

            System.out.println(result.text());
        }
    }
}
```

Source options, set between `from*` and `to*`, change how the source is read: its label size, which
page or label to read, the data that fills its variable fields, and its resolution. Target options,
set between `to*` and `execute()`, change what is written: the output resolution, rotation, and
format-specific output settings. Each stage offers only the options that act on it, and a named
`from*` or `to*` method offers only the options for that format, so `fromZpl(zpl).withPage(0)`
doesn't compile.

The SDK sends only the options you set. Every other option takes the server default.

### Fill variable fields

Each record produces one label:

```java
ConversionResult result = client.convert()
        .fromZpl(template)
            .withData(
                    Map.of("name", "ACME Corp", "sku", "12345"),
                    Map.of("name", "Globex", "sku", "67890"))
        .toPdf()
        .execute();                     // a 2-page PDF
```

### Change the printer resolution

To move a label made for a 300 dpi printer to a 203 dpi printer at the same physical size, set the
resolution on both stages:

```java
ConversionResult result = client.convert()
        .fromZpl(zplFor300Dpi)
            .withDpi(300)               // the printer the job was made for
        .toZpl()
            .withDpi(203)               // the printer it's going to
        .execute();
```

### Split a sheet into labels

This example cuts a Letter sheet of four labels into separate labels and lays them out two across
for a 2-up roll:

```java
ConversionResult result = client.convert()
        .fromPdf(sheet)
            .withSplit(s -> s.rows(2).columns(2).skipBlank(true))
        .toZpl()
            .withLayout(l -> l.across(2).gap(0.125f))   // inches
        .execute();
```

### Read the result

`result.text()` decodes the response with its charset, and is meant for ZPL, XML, and JSON. For
PDF, image, and printer-language output, use `result.bytes()` or `result.save(path)`. EPL, IPL, TSPL,
DPL, and SBPL output can carry raw binary graphics (EPL's `GW`, TSPL's `BITMAP`, IPL's
`STX`/`ETX`-framed bitmap columns, and SBPL's inline graphics), and decoding it as text corrupts them.
`result.requestId()` returns the request ID to quote to LabelZoom support.

## Print a label

A Cloud Print printer is a target, like `toZpl()`. The printer's configuration sets its language,
and LabelZoom converts the document to it, so a PDF prints on a ZPL printer. Printing needs an API
key with the `print` scope.

To print a document, call `toPrinter` after any source options:

```java
PrintJob job = client.convert()
        .fromPdf(Files.readAllBytes(Path.of("shipping-label.pdf")))
            .withPage(0)
        .toPrinter("PRINTER_ID")
            .withCopies(2)
            .withIdempotencyKey("SO-1001")
        .execute();

System.out.println(job.jobId() + " " + job.status());   // "queued" or "dispatched"
```

To print a published template, pass one record per label:

```java
PrintBatch batch = client.convert()
        .fromTemplate("TEMPLATE_ID")
            .withData(
                    Map.of("orderNumber", "SO-1001", "sku", "A-100"),
                    Map.of("orderNumber", "SO-1002", "sku", "B-200"))
            .withValidate(true)                 // reject missing merge fields before printing
        .toPrinter("PRINTER_ID")
            .withIdempotencyKey("BATCH-1001")
        .execute();

if (batch.remaining() > 0) {
    // Send the same request with the same idempotency key to submit the rest.
}
```

To follow a job, get it by ID:

```java
PrintJobDetails details = client.jobs().get(job.jobId());
if (details.status().equals("failed")) {
    System.err.println(details.error().orElse("unknown error"));
}
```

To check the daily job limit before printing, get the quota:

```java
PrintQuotaCounter jobsToday = client.getPrintQuota().jobsToday();
System.out.println(jobsToday.remaining() + " of " + jobsToday.limit() + " jobs left today");
```

| Topic | Behavior |
|---|---|
| Sources | Every source format prints, including NiceLabel, BarTender, and DYMO files, except a URL and base64 text. For those two, `toPrinter` throws a `LabelZoomValidationException` with parameter `source`. Fetch or decode the document and print its bytes. |
| Options | A document print takes every source option. The printer target offers `withRotation(degrees)`, a multiple of 90, and no other target option. A template takes merge data and `withValidate` only. |
| Copies | `withCopies(n)`, from 1 to 9999, sends the whole job to the printer `n` times, and multiplies any quantity in the document itself: `^PQ3` with 5 copies prints 15 labels. |
| Idempotency keys | With `withIdempotencyKey`, such as an order number, repeating the call returns the original job instead of printing again, and `job.isIdempotent()` is `true`. Without one, the SDK generates a key per call and reuses it on that call's retries. Keys are scoped to the account, so a deliberate reprint needs a new key. |
| Template batches | The server gives each record its own key, derived from yours. If `batch.remaining()` isn't `0`, send the same request with the same key: only the records that didn't print are submitted. The batch size is limited by `getPrintQuota().limits().maxBatchSize()`. |
| Daily limit | When the account's daily job limit is used up, `execute()` throws `LabelZoomPrintQuotaExceededException`. The SDK doesn't retry it, because the limit resets at 00:00 UTC. |

For the endpoints and limits, see the
[Cloud Print API reference](https://docs.labelzoom.com/reference/cloud-print-api/).

## Formats

**Sources:** `ZPL` `EPL` `IPL` `TSPL` `DPL` `SBPL` `ESCLABEL` `FINGERPRINT` `EZPL` `CPCL` `TPCL` `NLBL` `LBL` `BTW` `DYMO` `XML` `JSON` `PDF` `PNG` `BMP` `GIF` `JPEG` `JPG` `URL`

**Targets:** `ZPL` `EPL` `IPL` `TSPL` `DPL` `SBPL` `CPCL` `XML` `JSON` `PDF` `PNG` `BMP` `GIF` `JPEG`

- ESC/Label, Fingerprint, EZPL, TPCL, NiceLabel (`NLBL`, `LBL`), BarTender (`BTW`), and DYMO are
  source-only. Send NiceLabel and BarTender files as bytes.
- EPL, IPL, TSPL, DPL, SBPL, and CPCL are targets as well as sources, so `fromPdf(bytes).toEpl()` is
  a conversion. Their output is `text/plain` with every label concatenated. CPCL writes graphics as
  hex, so its output is plain text; read the others with `result.bytes()` whenever a label might
  carry graphics.
- `SourceFormat` and `TargetFormat` are separate enums. `JPG` and `URL` are source-only: `JPG`
  normalizes to `JPEG`, and `URL` is a fetch instruction rather than a format. So `toUrl()` doesn't
  exist, and `to(SourceFormat.PDF)` doesn't compile.
- `fromUrl` has the server fetch the URL you supply. If the URL comes from untrusted input,
  validate it first.

## Options by stage

### Source options

Set these after `from*` and before `to*`.

| Method | Source | What it does |
|---|---|---|
| `withLabelSize(float, float)` | Every source | Sets the label width and height in inches, not dots. It replaces the size the source declares. |
| `withLabelIndex(int)` | Every source | Converts only this label, counted from 0, on every plan. For an image or PDF, labels are counted after any split, page by page. |
| `withData(Map...)`, `withData(List)` | Every source | Fills the label's variable fields. Each record produces one label. |
| `withDpi(int)` | `fromZpl`, `fromEscLabel`, the other printer languages, and images | Sets the resolution the job or image was made for. The server default is 203. |
| `withPage(int)` | `fromPdf` | Converts only this page, counted from 0. Omit it to convert every page. |
| `withConversionMode(PdfConversionMode)` | `fromPdf` | `IMAGE` (the default) or `NATIVE`. |
| `withSplit(s -> ...)` | `fromPdf`, `fromPng`, `fromBmp`, `fromGif`, `fromJpeg` | Cuts each page into a grid of labels, for a printer-language target in `IMAGE` mode. Fields: `rows`, `columns`, `labelSize`, `margins`, `gaps` (all in inches), and `skipBlank`. |
| `withRaster(r -> ...)` | `fromPdf`, `fromPng`, `fromBmp`, `fromGif`, `fromJpeg` | Sets how the picture is rasterized. Fields: `scaling` (percent), `position` (output dots from the label's top-left corner; a negative offset crops), `darkness` (0 to 100, default 70), and `colorMode` (`GRAYSCALE`, the default, dithers). |
| `withCommandsToIgnore(String...)` | `fromZpl`, `fromEscLabel` | Skips these commands, for example `"^PQ"`. |

### Target options

Set these after `to*` and before `execute()`.

| Method | Target | What it does |
|---|---|---|
| `withDpi(int)` | Every target except `toPdf` | Sets the output resolution. The server default is the source's resolution, else 203. |
| `withRotation(int)` | Every target, including a printer | Rotates the finished label clockwise by a multiple of 90 degrees. Other values are rejected locally. |
| `withWatermark(boolean)` | Every target | Requests a watermark. The free tier always watermarks. |
| `withDialect(String)` | Every target | Selects a printer dialect, for example `moca`. Requires a paid plan. |
| `withParameter(String, Object)` | Every target | Sets an option the SDK doesn't model. The server ignores keys it doesn't know. |
| `withRawQueryParameter(String, String)` | Every target | Adds a query-string parameter next to `params`. |
| `withImageCompression(ZplImageCompression)` | `toZpl` | `Z64` (the default) or `COMPRESSED_HEX`. |
| `withCollapseCopies(boolean)` | `toZpl` | For an image or PDF source, turns each run of identical labels into one label with a `^PQ` count. |
| `withLayout(l -> ...)` | `toZpl` | For an image or PDF source in `IMAGE` mode, places labels side by side on each row. Fields: `across`, `gap` (inches), and `fill` (`SEQUENTIAL` or `REPEAT`). |
| `withColorMode(ColorMode)`, `withDarkness(int)` | `toPng`, `toBmp`, `toGif`, `toJpeg` | `BW` renders black and white at the darkness threshold; any other mode renders in color. |

The printer target offers `withRotation`, `withCopies`, and `withIdempotencyKey`. The template
printer target offers `withCopies` and `withIdempotencyKey`.

### Formats chosen at run time

`from(SourceFormat, ...)`, `fromFile`, `fromBase64Text`, and `fromUrl` return a
`DynamicSourceBuilder`, and `to(TargetFormat)` returns a `DynamicTargetBuilder`. Because the format
isn't known at compile time, these builders offer every option, and the format-specific ones carry
the format in their names: `withPdfPage`, `withPdfConversionMode`, `withZplCommandsToIgnore`,
`withZplImageCompression`, and `withZplCollapseCopies`, plus `withSplit`, `withRaster`, `withLayout`,
`withColorMode`, and `withDarkness`. The server ignores an option that doesn't apply to the format.

```java
ConversionResult result = client.convert()
        .fromFile(SourceFormat.PDF, Path.of("shipping-label.pdf"))
            .withPdfPage(0)
        .to(TargetFormat.ZPL)
        .execute();
```

## Errors

Every non-2xx response throws a typed exception. All of them extend `LabelZoomException`, which
extends `RuntimeException`.

| Exception | Status |
|---|---|
| `LabelZoomBadRequestException` | `400` |
| `LabelZoomUnauthorizedException` | `401` |
| `LabelZoomForbiddenException` | `403`. `isPaidFeature()` is `true` when the operation needs a paid plan. |
| `LabelZoomNotFoundException` | `404` |
| `LabelZoomPayloadTooLargeException` | `413` |
| `LabelZoomUnprocessableException` | `422`, for example a print to a printer whose agent needs an update. |
| `LabelZoomRateLimitedException` | `429`. `retryAfterSeconds()` returns the `Retry-After` value. |
| `LabelZoomPrintQuotaExceededException` | `429` for the daily print job limit. It extends `LabelZoomRateLimitedException`. |
| `LabelZoomServerException` | `5xx` |
| `LabelZoomException` | Any other non-2xx status. |

Each exception carries `statusCode()`, `getMessage()` (the server's own message), `requestId()`,
`rawBody()`, and `code()`. `code()` returns the error's stable code, such as
`agent_update_required`. Branch on the code rather than on the message.

```java
try {
    client.convert().fromZpl(zpl).toJson().execute();
} catch (LabelZoomForbiddenException e) {
    if (e.isPaidFeature()) {
        // "JSON export is a paid feature", the most common free-tier failure.
        System.err.println(e.getMessage() + " (request " + e.requestId().orElse("?") + ")");
    }
} catch (LabelZoomException e) {
    System.err.println(e.statusCode() + " " + e.code().orElse("") + ": " + e.getMessage());
}
```

`LabelZoomValidationException` means the calling code passed an invalid value, such as a rotation
that isn't a multiple of 90 or a blank printer ID. It's thrown before any request is sent, and
`parameter()` names the option. It extends `IllegalArgumentException`, not `LabelZoomException`, so
a handler written for server failures doesn't catch it.

## Retries

The SDK retries `429` responses, `5xx` responses, and transport failures. It makes up to 3
attempts, with backoff of 1, 2, and 4 seconds and full jitter, and waits longer when `Retry-After`
asks for more. Other `4xx` responses throw at once, and so does the daily print limit.

To change the policy, configure the client:

```java
LabelZoomClient client = LabelZoomClient.builder()
        .maxRetries(0)                          // turn retries off
        .timeout(Duration.ofSeconds(30))
        .build();
```

## Android

Android has no `java.net.http`, which the default transport uses, so pass your own
`HttpTransport`. This one uses [OkHttp](https://square.github.io/okhttp/):

```java
import com.labelzoom.sdk.HttpTransport;
import java.io.IOException;
import okhttp3.OkHttpClient;
import okhttp3.RequestBody;

final class OkHttpTransport implements HttpTransport {

    private final OkHttpClient client;

    OkHttpTransport(OkHttpClient client) {
        this.client = client;
    }

    @Override
    public Response send(Request request) throws IOException {
        okhttp3.Request.Builder builder = new okhttp3.Request.Builder()
                .url(request.uri())
                // No media type here: the SDK sets Content-Type itself, below. A GET has no body.
                .method(request.method(),
                        request.body() == null ? null : RequestBody.create(request.body(), null));
        request.headers().forEach(builder::header);

        try (okhttp3.Response response = client.newCall(builder.build()).execute()) {
            return new Response(
                    response.code(), response.headers().toMultimap(), response.body().bytes());
        }
    }
}
```

```java
LabelZoomClient client = LabelZoomClient.builder()
        .apiKey("API_KEY")      // Android has no LABELZOOM_API_KEY environment variable
        .transport(new OkHttpTransport(new OkHttpClient()))
        .build();
```

- Set timeouts on the `OkHttpClient`. `LabelZoomClient.Builder.timeout` applies only to the
  default transport.
- `execute()` blocks, so call it off the main thread.
- Your app needs the `android.permission.INTERNET` permission.
- No R8 or ProGuard rule is needed. The jar ships one that covers the default transport's
  `java.net.http` references.
- Core library desugaring isn't needed.

## Test your own code

To test code that calls the SDK without opening sockets, implement `HttpTransport` to return canned
responses. To test retries without waiting, turn off jitter and replace the sleeper:

```java
LabelZoomClient client = LabelZoomClient.builder()
        .transport(yourRecordingTransport)
        .useJitter(false)
        .sleeper(millis -> recorded.add(millis))
        .build();
```

## Migrate from 1.x

In 1.x, every option was set after `to*`. In 2.0, source options move before `to*`, and the raster
options move into a group.

| 1.x | 2.0 |
|---|---|
| `.toZpl().withPdfPage(0)` | `.fromPdf(pdf).withPage(0).toZpl()` |
| `.withPdfConversionMode(m)` | `.fromPdf(pdf).withConversionMode(m)` |
| `.withZplCommandsToIgnore("^PQ")` | `.fromZpl(zpl).withCommandsToIgnore("^PQ")` |
| `.withLabelSize(w, h)`, `.withData(...)` | The same methods, before `to*` |
| `.withZplImageCompression(c)` | `.toZpl().withImageCompression(c)` |
| `.withDpi(d)`, which set both resolutions | `.withDpi(d)` on the target stage for the output resolution. If the source was made for `d`, also call `.withDpi(d)` on the source stage. A PDF target takes no `withDpi`. |
| `.withScaling(p)`, `.withPosition(x, y)` | `.fromPdf(pdf).withRaster(r -> r.scaling(p).position(x, y))`, or the same on `fromPng` and the other image sources |
| `.withDarkness(d)`, `.withColorMode(m)` | `withRaster(r -> r.darkness(d).colorMode(m))` for an image or PDF source; `withDarkness` and `withColorMode` on `toPng` and the other image targets for black-and-white output |
| `.withRotation(r)`, `.withWatermark(w)`, `.withDialect(d)`, `.withParameter(k, v)`, `.withRawQueryParameter(k, v)` | Unchanged, on the target stage |
| `ConversionSourceBuilder`, `ConversionTargetBuilder` | `SourceStage` and `TargetStage` subclasses, such as `PdfSourceBuilder` and `ZplTargetBuilder`. A format chosen at run time returns `DynamicSourceBuilder` and `DynamicTargetBuilder`, which keep the format-prefixed names such as `withPdfPage`. |
| Not available | `.toPrinter(printerId)` on any source stage, `fromTemplate(templateId)`, `client.jobs().get(jobId)`, and `client.getPrintQuota()` |

## Contributing

To build the SDK and run its tests, run:

```sh
./gradlew build
```

The build needs no API key and calls no API. It compiles with `-Xlint:all -Werror`, checks the
Javadoc, and checks the main classes against the Android API level 26 signatures.

The test suite runs the shared [conformance fixtures](../conformance/) that every LabelZoom SDK is
checked against, and asserts that it ran all of them. To run only the conformance suite, run:

```sh
./gradlew test --tests '*Conformance*'
```

Java declares one skip, `validation/data-element-not-an-object`: `withData` accepts only
`Map<String, ?>`, so a non-object element can't be written. The compiler enforces that guarantee,
which is stronger than the runtime check the fixture asserts. See
[docs/CONFORMANCE.md](../docs/CONFORMANCE.md).

## License

MIT. See [LICENSE](../LICENSE).
