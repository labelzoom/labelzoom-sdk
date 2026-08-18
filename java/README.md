![LabelZoom Logo](../docs/LabelZoom_Logo_f_400px.png)

# LabelZoom Java SDK

Official Java client for the [LabelZoom API](https://api.labelzoom.com). Converts barcode labels
between ZPL, EPL, TSPL, DPL, PDF, LabelZoom XML/JSON, and raster images.

**Java 17+. Zero runtime dependencies.**

That second point is deliberate. This SDK gets dropped into WMS, ERP, and TMS deployments whose
classpaths are already crowded, and a transitive Jackson or Gson is a genuine source of version
conflicts. The small amount of JSON the SDK needs is built in.

## Install

```gradle
implementation 'com.labelzoom:labelzoom-sdk:0.1.2'
```

```xml
<dependency>
    <groupId>com.labelzoom</groupId>
    <artifactId>labelzoom-sdk</artifactId>
    <version>0.1.2</version>
</dependency>
```

> **Pre-1.0.** The public API is stable in practice and covered by a shared conformance suite, but
> it stays on `0.x` until all seven language SDKs have validated the same contract.

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

PDF to ZPL, one page, at a fixed label size:

```java
ConversionResult result = client.convert()
        .fromFile(SourceFormat.PDF, Path.of("shipping-label.pdf"))
        .toZpl()
        .withLabelSize(4f, 6f)      // inches
        .withPdfPage(0)             // 0-based; omit for every page
        .execute();

System.out.println(result.text());
```

Filling variable fields — **each record produces one label**:

```java
ConversionResult result = client.convert()
        .fromZpl(template)
        .toPdf()
        .withData(
                Map.of("name", "ACME Corp", "sku", "12345"),
                Map.of("name", "Globex", "sku", "67890"))
        .execute();                 // a 2-page PDF
```

## Formats

**Sources (12, plus `URL`):** `ZPL` `EPL` `TSPL` `DPL` `XML` `JSON` `PDF` `PNG` `BMP` `GIF` `JPEG` `JPG`

**Targets (8):** `ZPL` `XML` `JSON` `PDF` `PNG` `BMP` `GIF` `JPEG`

`SourceFormat` and `TargetFormat` are distinct enums, so `toEpl()` does not exist and
`to(SourceFormat.PDF)` does not compile. EPL, TSPL and DPL are source-only on the server, and the
type system says so rather than letting you find out from a 404.

`SourceFormat.URL` has the *server* fetch a URL you supply. Validate it first if it came from
untrusted input.

## Options

| Method | Notes |
|---|---|
| `withDpi(int)` | server default 203 |
| `withRotation(int)` | multiple of 90; rejected locally otherwise |
| `withScaling(float)` | percent, server default 100 |
| `withColorMode(ColorMode)` | `BW`, `GRAYSCALE` (default), `COLOR` |
| `withDarkness(int)` | 0–100, server default 70 |
| `withPosition(int, int)` | pixel offset of the extracted region |
| `withWatermark(boolean)` | forced on for the free tier regardless |
| `withDialect(String)` | e.g. `moca`; **paid** |
| `withLabelSize(float, float)` | **inches**, not dots |
| `withPdfConversionMode(PdfConversionMode)` | `IMAGE` (default) or `NATIVE` |
| `withPdfPage(int)` | **0-based**; omit to convert every page |
| `withZplCommandsToIgnore(String...)` | e.g. `"^PQ"` |
| `withZplImageCompression(ZplImageCompression)` | `Z64` (default) or `COMPRESSED_HEX` |
| `withData(Map...)` | one label per record |
| `withParameter(String, Object)` | escape hatch for anything not modeled yet |

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
honouring a longer `Retry-After`. Other 4xx surface immediately.

```java
LabelZoomClient.builder()
        .maxRetries(0)                        // disable
        .timeout(Duration.ofSeconds(30))
        .build();
```

## Testing your own code

`HttpTransport` is the seam — implement it to serve canned responses with no sockets. `sleeper`
and `useJitter` make retry behaviour testable without spending the wall-clock time.

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
