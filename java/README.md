![LabelZoom Logo](../docs/LabelZoom_Logo_f_400px.png)

# LabelZoom Java SDK

Official Java client for the [LabelZoom API](https://api.labelzoom.com). Converts barcode labels
between ZPL, EPL, IPL, TSPL, DPL, SBPL, PDF, LabelZoom XML/JSON, and raster images.

**Java 17+, or Android 8.0 (API 26)+. Zero runtime dependencies.**

That second point is deliberate. This SDK gets dropped into WMS, ERP, and TMS deployments whose
classpaths are already crowded, and a transitive Jackson or Gson is a genuine source of version
conflicts. The small amount of JSON the SDK needs is built in.

## Install

[![Maven Central](https://img.shields.io/maven-central/v/com.labelzoom/labelzoom-sdk)](https://central.sonatype.com/artifact/com.labelzoom/labelzoom-sdk)
— substitute that version for `VERSION` below.

```gradle
implementation 'com.labelzoom:labelzoom-sdk:VERSION'
```

```xml
<dependency>
    <groupId>com.labelzoom</groupId>
    <artifactId>labelzoom-sdk</artifactId>
    <version>VERSION</version>
</dependency>
```

> **Stable.** The public API is covered by a shared conformance suite that every language SDK
> runs against the same fixtures. Each SDK is versioned independently, and the contract carries
> its own version in `conformance/spec.json`.

## Quick start

**An API key is optional.** Without one you get the free tier — watermarked output, first label
only, a 1 MB request cap, and no multi-page, JSON-target, or image-to-image conversion.

```java
try (LabelZoomClient client = LabelZoomClient.builder().build()) {   // anonymous; this works
    ConversionResult result = client.convert()
            .fromZpl("^XA^FO20,20^A0N,28^FDHello^FS^XZ")
            .toPng()
                .withDpi(300)
            .execute();

    result.save(Path.of("label.png"));
}
```

With a key — explicitly, or picked up from `LABELZOOM_API_KEY`:

```java
LabelZoomClient.builder().apiKey("lz_live_...").build();
LabelZoomClient.builder().build();          // reads LABELZOOM_API_KEY
LabelZoomClient.builder().apiKey("").build(); // forces anonymous
```

A conversion is built in two stages. `from*` picks the source and returns the **source stage**,
where you set options that change how the source is read. `to*` picks the target and returns the
**target stage**, where you set options that change the output and then call `execute()`. Each
stage offers only the options that act on its side, and a named `from*` or `to*` offers only the
options for that format, so `fromZpl(zpl).withPage(0)` doesn't compile.

PDF to ZPL, one page, at a fixed label size:

```java
ConversionResult result = client.convert()
        .fromPdf(Files.readAllBytes(Path.of("shipping-label.pdf")))
            .withPage(0)                // 0-based; omit for every page
            .withLabelSize(4f, 6f)      // inches
        .toZpl()
            .withDpi(203)
        .execute();

System.out.println(result.text());
```

A Letter sheet of four labels, printed two across on a 2-up roll:

```java
ConversionResult result = client.convert()
        .fromPdf(sheet)
            .withSplit(s -> s.rows(2).columns(2).skipBlank(true))
        .toZpl()
            .withLayout(l -> l.across(2).gap(0.125f))   // inches
        .execute();
```

To move a label made for a 300 dpi printer to a 203 dpi one at the same physical size, set the
resolution on both stages:

```java
ConversionResult result = client.convert()
        .fromZpl(zplFor300Dpi)
            .withDpi(300)               // what the job was made for
        .toZpl()
            .withDpi(203)               // the printer it's going to
        .execute();
```

Filling variable fields — **each record produces one label**:

```java
ConversionResult result = client.convert()
        .fromZpl(template)
            .withData(
                    Map.of("name", "ACME Corp", "sku", "12345"),
                    Map.of("name", "Globex", "sku", "67890"))
        .toPdf()
        .execute();                     // a 2-page PDF
```

## Formats

**Sources:** `ZPL` `EPL` `IPL` `TSPL` `DPL` `SBPL` `XML` `JSON` `PDF` `PNG` `BMP` `GIF` `JPEG` `JPG` `URL`

**Targets:** `ZPL` `EPL` `IPL` `TSPL` `DPL` `SBPL` `XML` `JSON` `PDF` `PNG` `BMP` `GIF` `JPEG`

`SourceFormat` and `TargetFormat` are distinct enums, so `toUrl()` does not exist and
`to(SourceFormat.PDF)` does not compile. `JPG` and `URL` are source-only — `JPG` normalizes to
`JPEG`, and `URL` is a fetch instruction rather than a format — and the type system says so rather
than letting you find out from a 404.

`epl`, `ipl`, `tspl`, `dpl` and `sbpl` are targets as well as sources — ``fromPdf(bytes).toEpl()`` is a real conversion. Their
output is `text/plain` with every label concatenated, but they can all carry raw binary (EPL's `GW`,
TSPL's `BITMAP`, IPL's `STX`/`ETX`-framed bitmap columns, SBPL's inline graphics): read `result.bytes()` rather than `result.text()` whenever a label might carry graphics.

`SourceFormat.URL` has the *server* fetch a URL you supply. Validate it first if it came from
untrusted input.

## Options

### Source stage

Set after `from*`, before `to*`.

| Method | On | Notes |
|---|---|---|
| `withDpi(int)` | printer-language and image sources | the resolution the job or image was made for; server default 203 |
| `withLabelSize(float, float)` | every source | **inches**, not dots; replaces the size the source declares |
| `withLabelIndex(int)` | every source | **0-based**; converts only this label, on every plan |
| `withData(Map...)` | every source | one label per record |
| `withPage(int)` | `fromPdf` | **0-based**; omit to convert every page |
| `withConversionMode(PdfConversionMode)` | `fromPdf` | `IMAGE` (default) or `NATIVE` |
| `withSplit(s -> ...)` | `fromPdf`, `fromPng`, `fromBmp`, `fromGif`, `fromJpeg` | ZPL target, `IMAGE` mode: cut each page into a grid of labels. `rows`, `columns`, `labelSize`, `margins`, `gaps` (all **inches**), `skipBlank` |
| `withRaster(r -> ...)` | `fromPdf`, `fromPng`, `fromBmp`, `fromGif`, `fromJpeg` | how the picture is rasterized. `scaling` (percent), `position` (output dots from the label's top-left; negative crops), `darkness` (0–100, default 70), `colorMode` (`GRAYSCALE` dithers, the default) |
| `withCommandsToIgnore(String...)` | `fromZpl` | e.g. `"^PQ"` |

### Target stage

Set after `to*`, before `execute()`.

| Method | On | Notes |
|---|---|---|
| `withDpi(int)` | every target but `toPdf` | the resolution of the output; server default is the source's, else 203 |
| `withRotation(int)` | every target | multiple of 90; rejected locally otherwise |
| `withWatermark(boolean)` | every target | forced on for the free tier regardless |
| `withDialect(String)` | every target | e.g. `moca`; **paid** |
| `withParameter(String, Object)` | every target | escape hatch for anything not modeled yet |
| `withImageCompression(ZplImageCompression)` | `toZpl` | `Z64` (default) or `COMPRESSED_HEX` |
| `withCollapseCopies(boolean)` | `toZpl` | image or PDF source: each run of identical labels becomes one label with a `^PQ` count |
| `withLayout(l -> ...)` | `toZpl` | image or PDF source, `IMAGE` mode: labels side by side per row. `across`, `gap` (**inches**), `fill` (`SEQUENTIAL` or `REPEAT`) |
| `withColorMode(ColorMode)`, `withDarkness(int)` | `toPng`, `toBmp`, `toGif`, `toJpeg` | `BW` renders black and white at the darkness threshold; anything else renders in color |

### Formats chosen at run time

`from(SourceFormat, ...)`, `fromFile`, `fromBase64Text` and `fromUrl` return a
`DynamicSourceBuilder`, and `to(TargetFormat)` returns a `DynamicTargetBuilder`. The compiler can't
know the format there, so they offer every option, named with the format it applies to:
`withPdfPage`, `withPdfConversionMode`, `withZplCommandsToIgnore`, `withZplImageCompression` and
`withZplCollapseCopies`, plus `withRaster`, `withColorMode` and `withDarkness`. The server ignores
an option that doesn't apply to the format.

### Migrating from 1.x

In 1.x every option was set after `to*`. In 2.0, move the source options before it.

| 1.x | 2.0 |
|---|---|
| `.toZpl().withPdfPage(0)` | `.fromPdf(pdf).withPage(0).toZpl()` |
| `.withPdfConversionMode(m)` | `.fromPdf(pdf).withConversionMode(m)` |
| `.withZplCommandsToIgnore("^PQ")` | `.fromZpl(zpl).withCommandsToIgnore("^PQ")` |
| `.withLabelSize(w, h)`, `.withLabelIndex(i)`, `.withData(...)` | same name, before `to*` |
| `.withZplImageCompression(c)` | `.toZpl().withImageCompression(c)` |
| `.withDpi(d)`, which set both resolutions | `.withDpi(d)` on the target stage for the output; add it on the source stage too if the job was made for `d` |
| `.withScaling(p)`, `.withPosition(x, y)` | `.fromPdf(pdf).withRaster(r -> r.scaling(p).position(x, y))`, or `fromPng` and the other images |
| `.withDarkness(d)`, `.withColorMode(m)` | in `withRaster` for an image or PDF source; on `toPng` and the other image targets for black-and-white output |
| `.withLayout(across, gap)`, `.withLayoutFill(f)` | `.toZpl().withLayout(l -> l.across(across).gap(gap).fill(f))` |
| `.withSplit(r, c)` and the other `withSplit*` | `.withSplit(s -> s.rows(r).columns(c)...)` on the image or PDF source |
| `ConversionSourceBuilder`, `ConversionTargetBuilder` | `SourceStage` and `TargetStage` subclasses, such as `PdfSourceBuilder` and `ZplTargetBuilder` |

Only options you set are sent. The SDK never fills in a client-side default, so a change to a
server default reaches you without an SDK upgrade.

## Errors

Every non-2xx throws a typed exception carrying the server's own message, the raw body, and the
`X-LZ-Request-Id` support handle. The body is never discarded.

```java
try {
    client.convert().fromZpl(zpl).toJson().execute();
} catch (LabelZoomForbiddenException e) {
    if (e.isPaidFeature()) {
        // "JSON export is a paid feature" — the most common free-tier failure.
        System.err.println(e.getMessage() + " (request " + e.requestId().orElse("?") + ")");
    }
} catch (LabelZoomException e) {
    System.err.println(e.statusCode() + ": " + e.getMessage());
}
```

All the specific types extend `LabelZoomException`, which extends `RuntimeException` — a checked
exception on every `execute()` is hostile inside a fluent chain.

`LabelZoomValidationException` deliberately does **not** extend it. It means the calling code is
wrong, it never reaches the network, and it should not be swallowed by a handler written for
server failures.

## Retries

429, 5xx and transport failures are retried automatically: 3 attempts, 1s/2s/4s with full jitter,
honoring a longer `Retry-After`. Other 4xx surface immediately.

```java
LabelZoomClient.builder()
        .maxRetries(0)                        // disable
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
                // No media type here: the SDK sets Content-Type itself, below.
                .post(RequestBody.create(request.body(), null));
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
        .apiKey("lz_live_...")   // Android has no LABELZOOM_API_KEY environment variable
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

## Testing your own code

`HttpTransport` is the seam — implement it to serve canned responses with no sockets. `sleeper`
and `useJitter` make retry behavior testable without spending the wall-clock time.

```java
LabelZoomClient.builder()
        .transport(yourRecordingTransport)
        .useJitter(false)
        .sleeper(millis -> recorded.add(millis))
        .build();
```

## Development

```sh
./gradlew build          # offline; no key, no network
./gradlew test --tests '*Conformance*'
```

The suite runs the shared [conformance fixtures](../conformance/) that every LabelZoom SDK is
checked against, and asserts it executed all of them. See
[docs/CONFORMANCE.md](../docs/CONFORMANCE.md).

Java declares one skip, `validation/data-element-not-an-object`: `withData` accepts only
`Map<String, ?>`, so a non-object element cannot be written at all. The guarantee is enforced at
compile time and is strictly stronger than the runtime check the fixture asserts.

## License

MIT — see [LICENSE](../LICENSE).
