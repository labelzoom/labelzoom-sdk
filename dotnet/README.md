![LabelZoom Logo](../docs/LabelZoom_Logo_f_400px.png)

# LabelZoom .NET SDK

Official .NET client for the [LabelZoom API](https://api.labelzoom.com). Converts barcode labels
between ZPL, EPL, IPL, TSPL, DPL, SBPL, PDF, LabelZoom XML/JSON, and raster images.

Targets **netstandard2.0** (so .NET Framework 4.6.1+, .NET Core 2.0+, .NET 5+) and **net8.0**.

## Install

```sh
dotnet add package LabelZoom.Sdk
```

> **Stable.** The public API is covered by a shared conformance suite that every language SDK
> runs against the same fixtures. Each SDK is versioned independently, and the contract carries
> its own version in `conformance/spec.json`.

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

A conversion is built in two stages. `From*` picks the source and returns the **source stage**,
where you set options that change how the source is read. `To*` picks the target and returns the
**target stage**, where you set options that change the output and then call `ExecuteAsync`.
Each stage offers only the options that act on its side, and a named `From*` or `To*` offers only
the options for that format.

PDF to ZPL, one page, at a fixed label size:

```csharp
var result = await client.Convert()
    .FromPdf(File.ReadAllBytes("shipping-label.pdf"))
        .WithPage(0)                                // 0-based; omit for every page
        .WithLabelSize(widthInches: 4f, heightInches: 6f)
    .ToZpl()
        .WithDpi(203)
    .ExecuteAsync();

Console.WriteLine(result.Text);
```

A Letter sheet of four labels, printed two across on a 2-up roll:

```csharp
var result = await client.Convert()
    .FromPdf(sheet)
        .WithSplit(s => s.Rows(2).Columns(2).SkipBlank())
    .ToZpl()
        .WithLayout(l => l.Across(2).Gap(0.125f))   // inches
    .ExecuteAsync();
```

Filling variable fields — **each record produces one label**:

```csharp
var result = await client.Convert()
    .FromZpl(template)
        .WithData(
            new { name = "ACME Corp", sku = "12345" },
            new { name = "Globex",    sku = "67890" })
    .ToPdf()
    .ExecuteAsync();                                // a 2-page PDF
```

## Formats

**Sources:** `Zpl` `Epl` `Ipl` `Tspl` `Dpl` `Sbpl` `Xml` `Json` `Pdf` `Png` `Bmp` `Gif` `Jpeg` `Jpg` (an alias for `Jpeg`) `Url`

**Targets:** `Zpl` `Epl` `Ipl` `Tspl` `Dpl` `Sbpl` `Xml` `Json` `Pdf` `Png` `Bmp` `Gif` `Jpeg`

`SourceFormat` and `TargetFormat` are distinct types, so `.ToUrl()` does not exist and
`To(SourceFormat.Pdf)` does not compile. `Jpg` and `Url` are source-only — `Jpg` normalizes to
`Jpeg`, and `Url` is a fetch instruction rather than a format — and the type system says so rather
than letting you find out from a 404.

`epl`, `ipl`, `tspl`, `dpl` and `sbpl` are targets as well as sources — ``.FromPdf(bytes).ToEpl()`` is a real conversion. Their
output is `text/plain` with every label concatenated, but they can all carry raw binary (EPL's `GW`,
TSPL's `BITMAP`, IPL's `STX`/`ETX`-framed bitmap columns, SBPL's inline graphics): read `result.Bytes` rather than `result.Text` whenever a label might carry graphics.

`SourceFormat.Url` has the *server* fetch a URL you supply and convert whatever it finds. Validate
the URL first if it came from untrusted input.

## Options

### Source stage

Set after `From*`, before `To*`.

| Method | On | Notes |
|---|---|---|
| `WithLabelSize(float, float)` | every source | **inches**, not dots; replaces the size the source declares |
| `WithLabelIndex(int)` | every source | **0-based**; converts only this label, on every plan |
| `WithData(params object?[])` | every source | one label per record |
| `WithPage(int)` | `FromPdf` | **0-based**; omit to convert every page |
| `WithConversionMode(PdfConversionMode)` | `FromPdf` | `Image` (default) or `Native` |
| `WithSplit(s => …)` | `FromPdf`, `FromPng`, `FromBmp`, `FromGif`, `FromJpeg` | ZPL target, `Image` mode: cut each page into a grid of labels. `Rows`, `Columns`, `LabelSize`, `Margins`, `Gaps` (all **inches**), `SkipBlank` |
| `WithCommandsToIgnore(params string[])` | `FromZpl` | e.g. `"^PQ"` |

### Target stage

Set after `To*`, before `ExecuteAsync`.

| Method | On | Notes |
|---|---|---|
| `WithDpi(int)` | every target | server default 203 |
| `WithRotation(int)` | every target | must be a multiple of 90; rejected locally otherwise |
| `WithScaling(float)` | every target | percent, server default 100 |
| `WithColorMode(ColorMode)` | every target | `Bw`, `Grayscale` (default), `Color` |
| `WithDarkness(int)` | every target | 0–100, server default 70 |
| `WithPosition(int, int)` | every target | pixel offset of the extracted region |
| `WithWatermark(bool)` | every target | forced on for the free tier regardless |
| `WithDialect(string)` | every target | e.g. `moca`; **paid** |
| `WithParameter(string, object?)` | every target | escape hatch for anything not modeled yet |
| `WithImageCompression(ZplImageCompression)` | `ToZpl` | `Z64` (default) or `CompressedHex` |
| `WithCollapseCopies(bool)` | `ToZpl` | image or PDF source: each run of identical labels becomes one label with a `^PQ` count |
| `WithLayout(l => …)` | `ToZpl` | image or PDF source, `Image` mode: labels side by side per row. `Across`, `Gap` (**inches**), `Fill` (`Sequential` or `Repeat`) |

### Formats chosen at run time

`From(SourceFormat, …)`, `FromFile`, `FromBase64Text` and `FromUrl` return a
`DynamicSourceBuilder`, and `To(TargetFormat)` returns a `DynamicTargetBuilder`. The compiler can't
know the format there, so they offer every option, named with the format it applies to:
`WithPdfPage`, `WithPdfConversionMode`, `WithZplCommandsToIgnore`, `WithZplImageCompression` and
`WithZplCollapseCopies`. The server ignores an option that doesn't apply to the format.

### Migrating from 1.x

In 1.x every option was set after `To*`. In 2.0, move the source options before it.

| 1.x | 2.0 |
|---|---|
| `.ToZpl().WithPdfPage(0)` | `.FromPdf(pdf).WithPage(0).ToZpl()` |
| `.WithPdfConversionMode(m)` | `.FromPdf(pdf).WithConversionMode(m)` |
| `.WithZplCommandsToIgnore("^PQ")` | `.FromZpl(zpl).WithCommandsToIgnore("^PQ")` |
| `.WithLabelSize(w, h)`, `.WithLabelIndex(i)`, `.WithData(...)` | same name, before `To*` |
| `.WithZplImageCompression(c)` | `.ToZpl().WithImageCompression(c)` |
| `ConversionSourceBuilder`, `ConversionTargetBuilder` | `SourceStage<T>` and `TargetStage<T>` subclasses, such as `PdfSourceBuilder` and `ZplTargetBuilder` |

`FromFile(SourceFormat.Pdf, path)` still works, and returns a `DynamicSourceBuilder` that keeps the
format-prefixed names.

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
honoring a longer `Retry-After`. Other 4xx responses are returned immediately — a malformed
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
