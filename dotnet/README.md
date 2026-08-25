![LabelZoom Logo](../docs/LabelZoom_Logo_f_400px.png)

# LabelZoom .NET SDK

Official .NET client for the [LabelZoom API](https://api.labelzoom.com). Converts barcode labels
between ZPL, EPL, TSPL, DPL, PDF, LabelZoom XML/JSON, and raster images.

Targets **netstandard2.0** (so .NET Framework 4.6.1+, .NET Core 2.0+, .NET 5+) and **net8.0**.

## Install

```sh
dotnet add package LabelZoom.Sdk
```

> **Stable at `1.0.0`.** The public API is covered by a shared conformance suite that
> every language SDK runs against the same fixtures, and it is versioned independently of
> the other SDKs — the contract carries its own version in `conformance/spec.json`.

<details>
<summary>Build from source</summary>

```sh
git clone https://github.com/labelzoom/labelzoom-sdk.git
cd labelzoom-sdk/dotnet
dotnet build LabelZoom.Sdk.sln
```
</details>

## Quick start

**An API key is optional.** Without one you get the free tier — watermarked output, first label
only, a 1 MB request cap, and no multi-page, JSON-target, or image-to-image conversion.

```csharp
using LabelZoom.Sdk;

using var client = new LabelZoomClient();          // anonymous; this works

var result = await client.Convert()
    .FromZpl("^XA^FO20,20^A0N,28^FDHello^FS^XZ")
    .ToPng()
    .WithDpi(300)
    .ExecuteAsync();

result.Save("label.png");
```

With a key — passed explicitly, or picked up from `LABELZOOM_API_KEY`:

```csharp
using var client = new LabelZoomClient("lz_live_...");
using var fromEnvironment = new LabelZoomClient();  // reads LABELZOOM_API_KEY
```

PDF to ZPL, one page, at a fixed label size:

```csharp
var result = await client.Convert()
    .FromFile(SourceFormat.Pdf, "shipping-label.pdf")
    .ToZpl()
    .WithLabelSize(widthInches: 4f, heightInches: 6f)
    .WithPdfPage(0)                                 // 0-based; omit for every page
    .ExecuteAsync();

Console.WriteLine(result.Text);
```

Filling variable fields — **each record produces one label**:

```csharp
var result = await client.Convert()
    .FromZpl(template)
    .ToPdf()
    .WithData(
        new { name = "ACME Corp", sku = "12345" },
        new { name = "Globex",    sku = "67890" })
    .ExecuteAsync();                                // a 2-page PDF
```

## Formats

**Sources (12, plus `Url`):** `Zpl` `Epl` `Tspl` `Dpl` `Xml` `Json` `Pdf` `Png` `Bmp` `Gif` `Jpeg` `Jpg` (an alias for `Jpeg`)

**Targets (11):** `Zpl` `Epl` `Tspl` `Dpl` `Xml` `Json` `Pdf` `Png` `Bmp` `Gif` `Jpeg`

`SourceFormat` and `TargetFormat` are distinct types, so `.ToUrl()` does not exist and
`To(SourceFormat.Pdf)` does not compile. `Jpg` and `Url` are source-only — `Jpg` normalizes to
`Jpeg`, and `Url` is a fetch instruction rather than a format — and the type system says so rather
than letting you find out from a 404.

`epl`, `tspl` and `dpl` are targets as well as sources — ``.FromPdf(bytes).ToEpl()`` is a real conversion. Their
output is `text/plain` with every label concatenated, but EPL's `GW` and TSPL's `BITMAP` commands
inline raw binary: read `result.Bytes` rather than `result.Text` whenever a label might carry graphics.

`SourceFormat.Url` has the *server* fetch a URL you supply and convert whatever it finds. Validate
the URL first if it came from untrusted input.

## Options

| Method | Notes |
|---|---|
| `WithDpi(int)` | server default 203 |
| `WithRotation(int)` | must be a multiple of 90; rejected locally otherwise |
| `WithScaling(float)` | percent, server default 100 |
| `WithColorMode(ColorMode)` | `Bw`, `Grayscale` (default), `Color` |
| `WithDarkness(int)` | 0–100, server default 70 |
| `WithPosition(int, int)` | pixel offset of the extracted region |
| `WithWatermark(bool)` | forced on for the free tier regardless |
| `WithDialect(string)` | e.g. `moca`; **paid** |
| `WithLabelSize(float, float)` | **inches**, not dots |
| `WithPdfConversionMode(PdfConversionMode)` | `Image` (default) or `Native` |
| `WithPdfPage(int)` | **0-based**; omit to convert every page |
| `WithZplCommandsToIgnore(params string[])` | e.g. `"^PQ"` |
| `WithZplImageCompression(ZplImageCompression)` | `Z64` (default) or `CompressedHex` |
| `WithData(params object?[])` | one label per record |
| `WithParameter(string, object?)` | escape hatch for anything not modeled yet |

Only options you actually set are sent. The SDK never fills in a client-side default, so a change
to a server default reaches you without an SDK upgrade.

## Errors

Every non-2xx becomes a typed exception carrying the server's own message, the raw body, and the
`X-LZ-Request-Id` support handle. The body is never discarded.

```csharp
try
{
    var result = await client.Convert().FromZpl(zpl).ToJson().ExecuteAsync();
}
catch (LabelZoomForbiddenException ex) when (ex.IsPaidFeature)
{
    // "JSON export is a paid feature" — the most common free-tier failure.
    Console.WriteLine($"{ex.Message} (request {ex.RequestId})");
}
catch (LabelZoomException ex)
{
    // One base type catches them all.
    Console.WriteLine($"{(int)ex.StatusCode}: {ex.Message}");
}
```

`LabelZoomBadRequestException`, `LabelZoomUnauthorizedException`, `LabelZoomForbiddenException`,
`LabelZoomNotFoundException`, `LabelZoomPayloadTooLargeException`, `LabelZoomRateLimitedException`
and `LabelZoomServerException` all derive from `LabelZoomException`.

`LabelZoomValidationException` deliberately does **not** — it means the calling code is wrong, it
never reaches the network, and it should not be swallowed by a `catch (LabelZoomException)` written
to handle server failures.

## Retries

429, 5xx and transport failures are retried automatically: 3 attempts, 1s/2s/4s with full jitter,
honouring a longer `Retry-After`. Other 4xx responses are returned immediately — a malformed
request will not become valid on a second attempt.

```csharp
using var client = new LabelZoomClient(new LabelZoomClientOptions
{
    MaxRetries = 0,                        // disable
    Timeout = TimeSpan.FromSeconds(30),
});
```

## Dependency injection

```csharp
services.AddHttpClient("labelzoom");
services.AddSingleton(sp => new LabelZoomClient(new LabelZoomClientOptions
{
    HttpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient("labelzoom"),
}));
```

`LabelZoomClient` is thread-safe and meant to be long-lived. When you supply your own
`HttpClient`, you own its lifetime and the SDK will not dispose it.

## Testing your own code

`LabelZoomClientOptions.HttpMessageHandler` is the stub seam, and `SleepAsync` lets retry logic be
tested without spending the wall-clock time:

```csharp
var options = new LabelZoomClientOptions
{
    HttpMessageHandler = yourStubHandler,
    UseJitter = false,
    SleepAsync = (delay, _) => { recorded.Add(delay); return Task.CompletedTask; },
};
```

## Running the tests

```sh
dotnet test --filter "Category!=Integration"    # offline; no key, no network
dotnet test --filter "Category=Integration"     # hits the real API
```

The offline suite runs the shared [conformance fixtures](../conformance/) that every LabelZoom SDK
is checked against, and asserts it executed all of them. See
[docs/CONFORMANCE.md](../docs/CONFORMANCE.md).

## License

MIT — see [LICENSE](../LICENSE).
