![LabelZoom Logo](../docs/LabelZoom_Logo_f_400px.png)

# LabelZoom .NET SDK

Official .NET client for the [LabelZoom API](https://api.labelzoom.com). Converts barcode labels
between ZPL, EPL, IPL, TSPL, DPL, SBPL, CPCL, PDF, LabelZoom XML/JSON, and raster images, and reads
ESC/Label, Fingerprint, EZPL, TPCL, NiceLabel, BarTender and DYMO files.

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

To move a label made for a 300 dpi printer to a 203 dpi one at the same physical size, set the
resolution on both stages:

```csharp
var result = await client.Convert()
    .FromZpl(zplFor300Dpi)
        .WithDpi(300)                               // what the job was made for
    .ToZpl()
        .WithDpi(203)                               // the printer it's going to
    .ExecuteAsync();
```

Set only the source stage's to say what a job was made for without rescaling it: the output
follows. A PDF has no dots, so `FromPdf` has no `WithDpi`, and neither does `ToPdf`.

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

**Sources:** `Zpl` `Epl` `Ipl` `Tspl` `Dpl` `Sbpl` `EscLabel` `Fingerprint` `Ezpl` `Cpcl` `Tpcl` `Nlbl` `Lbl` `Btw` `Dymo` `Xml` `Json` `Pdf` `Png` `Bmp` `Gif` `Jpeg` `Jpg` (an alias for `Jpeg`) `Url`

**Targets:** `Zpl` `Epl` `Ipl` `Tspl` `Dpl` `Sbpl` `Cpcl` `Xml` `Json` `Pdf` `Png` `Bmp` `Gif` `Jpeg`

The newer printer languages (ESC/Label, Fingerprint, EZPL, TPCL) and the design-file formats
(NiceLabel, BarTender, DYMO) are source-only. Send NiceLabel and BarTender files as bytes. CPCL is
a target too, and writes graphics as hex, so its output is plain text.

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
| `WithDpi(int)` | printer-language and image sources | the resolution the job or image was made for; server default 203 |
| `WithLabelSize(float, float)` | every source | **inches**, not dots; replaces the size the source declares |
| `WithLabelIndex(int)` | every source | **0-based**; converts only this label, on every plan |
| `WithData(params object?[])` | every source | one label per record |
| `WithPage(int)` | `FromPdf` | **0-based**; omit to convert every page |
| `WithConversionMode(PdfConversionMode)` | `FromPdf` | `Image` (default) or `Native` |
| `WithSplit(s => …)` | `FromPdf`, `FromPng`, `FromBmp`, `FromGif`, `FromJpeg` | ZPL target, `Image` mode: cut each page into a grid of labels. `Rows`, `Columns`, `LabelSize`, `Margins`, `Gaps` (all **inches**), `SkipBlank` |
| `WithRaster(r => …)` | `FromPdf`, `FromPng`, `FromBmp`, `FromGif`, `FromJpeg` | how the picture is rasterized. `Scaling` (percent), `Position` (output dots from the label's top-left; negative crops), `Darkness` (0–100, default 70), `ColorMode` (`Grayscale` dithers, the default) |
| `WithCommandsToIgnore(params string[])` | `FromZpl` | e.g. `"^PQ"` |

### Target stage

Set after `To*`, before `ExecuteAsync`.

| Method | On | Notes |
|---|---|---|
| `WithDpi(int)` | every target but `ToPdf` | the resolution of the output; server default is the source's, else 203 |
| `WithRotation(int)` | every target | must be a multiple of 90; rejected locally otherwise |
| `WithWatermark(bool)` | every target | forced on for the free tier regardless |
| `WithDialect(string)` | every target | e.g. `moca`; **paid** |
| `WithParameter(string, object?)` | every target | escape hatch for anything not modeled yet |
| `WithImageCompression(ZplImageCompression)` | `ToZpl` | `Z64` (default) or `CompressedHex` |
| `WithCollapseCopies(bool)` | `ToZpl` | image or PDF source: each run of identical labels becomes one label with a `^PQ` count |
| `WithLayout(l => …)` | `ToZpl` | image or PDF source, `Image` mode: labels side by side per row. `Across`, `Gap` (**inches**), `Fill` (`Sequential` or `Repeat`) |
| `WithColorMode(ColorMode)`, `WithDarkness(int)` | `ToPng`, `ToBmp`, `ToGif`, `ToJpeg` | `Bw` renders black and white at the darkness threshold; anything else renders in color |

To crop an image or PDF page, set its position with a negative offset and the size of the label it's
cut to:

```csharp
var result = await client.Convert()
    .FromPng(photo)
        .WithLabelSize(4f, 6f)
        .WithRaster(r => r.Position(-120, -80))     // trims 120 dots off the left, 80 off the top
    .ToZpl()
    .ExecuteAsync();
```

### Formats chosen at run time

`From(SourceFormat, …)`, `FromFile`, `FromBase64Text` and `FromUrl` return a
`DynamicSourceBuilder`, and `To(TargetFormat)` returns a `DynamicTargetBuilder`. The compiler can't
know the format there, so they offer every option, named with the format it applies to:
`WithPdfPage`, `WithPdfConversionMode`, `WithZplCommandsToIgnore`, `WithZplImageCompression` and
`WithZplCollapseCopies`, plus `WithRaster`, `WithColorMode` and `WithDarkness`. The server ignores
an option that doesn't apply to the format.

### Migrating from 1.x

In 1.x every option was set after `To*`. In 2.0, move the source options before it.

| 1.x | 2.0 |
|---|---|
| `.ToZpl().WithPdfPage(0)` | `.FromPdf(pdf).WithPage(0).ToZpl()` |
| `.WithPdfConversionMode(m)` | `.FromPdf(pdf).WithConversionMode(m)` |
| `.WithZplCommandsToIgnore("^PQ")` | `.FromZpl(zpl).WithCommandsToIgnore("^PQ")` |
| `.WithLabelSize(w, h)`, `.WithLabelIndex(i)`, `.WithData(...)` | same name, before `To*` |
| `.WithZplImageCompression(c)` | `.ToZpl().WithImageCompression(c)` |
| `.WithDpi(d)`, which set both resolutions | `.WithDpi(d)` on the target stage for the output; add it on the source stage too if the job was made for `d` |
| `.WithScaling(p)`, `.WithPosition(x, y)` | `.FromPdf(pdf).WithRaster(r => r.Scaling(p).Position(x, y))`, or `FromPng` and the other images |
| `.WithDarkness(d)`, `.WithColorMode(m)` | in `WithRaster` for an image or PDF source; on `ToPng` and the other image targets for black-and-white output |
| `ConversionSourceBuilder`, `ConversionTargetBuilder` | `SourceStage<T>` and `TargetStage<T>` subclasses, such as `PdfSourceBuilder` and `ZplTargetBuilder` |

`FromFile(SourceFormat.Pdf, path)` still works, and returns a `DynamicSourceBuilder` that keeps the
format-prefixed names.

Only options you actually set are sent. The SDK never fills in a client-side default, so a change
to a server default reaches you without an SDK upgrade.

## Print

Send a document to a printer connected through the LabelZoom Print Agent. A printer is a target,
like `ToZpl()`: the printer's own configuration sets its language, and LabelZoom converts the
document to it, so a PDF prints on a ZPL printer. Printing needs an API key with the `print` scope.

```csharp
using LabelZoom.Sdk.Printing;

var job = await client.Convert()
    .FromPdf(pdf)
        .WithPage(0)                       // any source options
    .ToPrinter(printerId)
        .WithCopies(2)
        .WithIdempotencyKey(orderNumber)
    .ExecuteAsync();                       // PrintJob { JobId, Status, Copies, Idempotent }

var batch = await client.Convert()
    .FromTemplate(templateId)
        .WithData(new { orderNumber = "SO-1001", sku = "A-100" })
    .ToPrinter(printerId)
    .ExecuteAsync();                       // PrintBatch { Jobs, Remaining }

var details = await client.Jobs.GetAsync(job.JobId);   // queued, dispatched, printing, completed, failed
var quota = await client.GetPrintQuotaAsync();         // quota.JobsToday.Remaining
```

- Every source format prints, including NiceLabel, BarTender and DYMO files. A URL and base64 text
  can't: decode or fetch the document and print its bytes.
- The printer target offers `WithRotation`. Other target options depend on the output language, which
  the printer sets.
- `WithCopies(n)` sends the whole job `n` times, from 1 to 9999. It multiplies a quantity in the
  document: `^PQ3` with 5 copies prints 15 labels.
- Every print carries an `Idempotency-Key`. Set your own with `WithIdempotencyKey` to make a repeated
  call return the original job instead of printing again. Without one, the SDK generates a key per
  call, so its own retries can't print twice.
- A template batch that runs out of time returns `Remaining`, the number of records not yet
  submitted. Send the same request with the same key to submit them.
- The daily job limit raises `LabelZoomPrintQuotaExceededException`, which is never retried: it
  resets at 00:00 UTC.

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
`LabelZoomNotFoundException`, `LabelZoomPayloadTooLargeException`, `LabelZoomUnprocessableException`
(422), `LabelZoomRateLimitedException` and `LabelZoomServerException` all derive from
`LabelZoomException`. `LabelZoomPrintQuotaExceededException` derives from
`LabelZoomRateLimitedException`. Each carries `Code`, the error's stable code such as
`agent_update_required`, or `null`; branch on it rather than on the message.

`LabelZoomValidationException` deliberately does **not** — it means the calling code is wrong, it
never reaches the network, and it should not be swallowed by a `catch (LabelZoomException)` written
to handle server failures.

## Retries

429, 5xx and transport failures are retried automatically: 3 attempts, 1s/2s/4s with full jitter,
honoring a longer `Retry-After`. The daily print limit is the exception: its 429 is thrown at once.
Other 4xx responses are returned immediately — a malformed
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
