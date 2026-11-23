![LabelZoom Logo](../docs/LabelZoom_Logo_f_400px.png)

# LabelZoom .NET SDK

The LabelZoom .NET SDK is the official C# client for the [LabelZoom API](https://api.labelzoom.com).
Use it to convert barcode labels between printer languages, PDF, images, and LabelZoom XML and JSON,
and to print labels on Cloud Print printers.

## What the SDK handles

- Retries for `429`, `5xx`, and transport failures, with backoff that honors `Retry-After`.
- Typed exceptions that carry the HTTP status, the server's message, the raw body, the error `code`,
  and the request ID.
- Binary-safe results: every result keeps the response bytes, so PDF, image, and printer-language
  output is never corrupted by text decoding.
- Idempotent printing: every print request carries an `Idempotency-Key`, so a retried request
  doesn't print twice.
- Calls without an API key. An anonymous client uses the watermarked free tier. See
  [Rate limits and plans](https://docs.labelzoom.com/reference/rate-limits-and-plans/).

## Install

The package targets `netstandard2.0` and `net8.0`, so it runs on .NET Framework 4.6.1 or later,
.NET Core 2.0 or later, and .NET 5 or later.

```sh
dotnet add package LabelZoom.Sdk
```

To build the SDK from source:

```sh
git clone https://github.com/labelzoom/labelzoom-sdk.git
cd labelzoom-sdk/dotnet
dotnet build LabelZoom.Sdk.sln
```

## Convert a label

A conversion is a pipeline: choose the source, set source options, choose the target, set target
options, and then call `ExecuteAsync`. This program converts the first page of a PDF to a 4 x 6 inch
ZPL label for a 203 dpi printer:

```csharp
using LabelZoom.Sdk;

using var client = new LabelZoomClient();               // reads LABELZOOM_API_KEY

var result = await client.Convert()
    .FromPdf(File.ReadAllBytes("shipping-label.pdf"))   // the source
        .WithPage(0)                                    // source options
        .WithLabelSize(widthInches: 4f, heightInches: 6f)
    .ToZpl()                                            // the target
        .WithDpi(203)                                   // target options
    .ExecuteAsync();

result.Save("shipping-label.zpl");
```

Source options change how the source document is read: its label size, which page or label to
convert, the data that fills its variable fields, and the resolution it was made for. Target options
change what is written: the output resolution, rotation, watermark, dialect, and format-specific
settings such as ZPL image compression. `From*` returns a source stage that offers only the options
for that format, and `To*` returns a target stage that does the same, so `FromZpl(zpl).WithPage(0)`
and `ToPdf().WithDpi(203)` don't compile. For every option, see [Options by stage](#options-by-stage).

The SDK sends only the options you set, and never fills in a default of its own. Every call is
asynchronous and takes an optional `CancellationToken`.

### Pass an API key

The client reads the `LABELZOOM_API_KEY` environment variable when you don't pass a key. Without a
key, it calls the API anonymously, on the free tier: watermarked output, the first label only, a 1 MB
request limit, and no multi-page, JSON-target, or image-to-image conversion.

```csharp
using var client = new LabelZoomClient("API_KEY");      // an lz_live_ or lz_test_ key, or a JWT
using var anonymous = new LabelZoomClient(new LabelZoomClientOptions { ApiKey = "" });
```

An empty `ApiKey` forces anonymous mode and skips the environment variable. `client.IsAuthenticated`
reports whether the client found a credential. `LabelZoomClient` is thread-safe: create one for your
application and reuse it.

### Read the result

| Member | What it holds |
|---|---|
| `Bytes` | The response body. Use it for PDF, image, EPL, IPL, TSPL, DPL, and SBPL output. |
| `Text` | The body decoded with the response charset, or UTF-8. Use it for ZPL, CPCL, XML, and JSON. |
| `Save(path)` | Writes `Bytes` to a file, replacing an existing one. |
| `ContentType`, `StatusCode`, `RequestId` | The response's media type, HTTP status, and `X-LZ-Request-Id`. |

### Move a label to another printer resolution

To convert a label made for a 300 dpi printer to a 203 dpi printer at the same physical size, set
the resolution on both stages:

```csharp
var result = await client.Convert()
    .FromZpl(zplFor300Dpi)
        .WithDpi(300)                               // the resolution the job was made for
    .ToZpl()
        .WithDpi(203)                               // the resolution of the printer it goes to
    .ExecuteAsync();
```

If you set only the source resolution, the output is written at that resolution too. A PDF has no
resolution, so `FromPdf` and `ToPdf` have no `WithDpi`.

### Fill variable fields

Each record produces one label:

```csharp
var result = await client.Convert()
    .FromZpl(template)
        .WithData(
            new { name = "ACME Corp", sku = "12345" },
            new { name = "Globex", sku = "67890" })
    .ToPdf()
    .ExecuteAsync();                                // a 2-page PDF
```

Records can be anonymous types, classes, or dictionaries. Property names are sent as written, so a
class property `Name` fills the field `Name`, not `name`.

### Split a sheet and lay out a roll

This request cuts a Letter sheet of four labels into a 2 x 2 grid and prints them two across on a
2-up roll:

```csharp
var result = await client.Convert()
    .FromPdf(sheet)
        .WithSplit(s => s.Rows(2).Columns(2).SkipBlank())
    .ToZpl()
        .WithLayout(l => l.Across(2).Gap(0.125f))   // inches
    .ExecuteAsync();
```

### Crop an image or a PDF page

To crop, set the label size and move the picture with a negative position:

```csharp
var result = await client.Convert()
    .FromPng(photo)
        .WithLabelSize(4f, 6f)
        .WithRaster(r => r.Position(-120, -80))     // trims 120 dots off the left and 80 off the top
    .ToZpl()
    .ExecuteAsync();
```

## Print a label

A printer is a target. `ToPrinter(printerId)` stands where `ToZpl()` would, and LabelZoom converts
the document to the language the printer is configured for, so a PDF prints on a ZPL printer.
Printing needs an API key with the `print` scope and a printer connected through the LabelZoom Print
Agent.

```csharp
using LabelZoom.Sdk;
using LabelZoom.Sdk.Printing;

using var client = new LabelZoomClient("API_KEY");

PrintJob job = await client.Convert()
    .FromPdf(File.ReadAllBytes("shipping-label.pdf"))
        .WithPage(0)                                // any source options
    .ToPrinter("PRINTER_ID")
        .WithCopies(2)
        .WithIdempotencyKey("SO-1001")
    .ExecuteAsync();                                // JobId, Status, Copies, Idempotent

PrintBatch batch = await client.Convert()
    .FromTemplate("TEMPLATE_ID")
        .WithData(new { orderNumber = "SO-1001", sku = "A-100" })
        .WithValidate()
    .ToPrinter("PRINTER_ID")
        .WithCopies(1)
        .WithIdempotencyKey("SO-1001-template")
    .ExecuteAsync();                                // Jobs, Remaining, Message

PrintJobDetails details = await client.Jobs.GetAsync(job.JobId);
Console.WriteLine(details.Status);                  // queued, dispatched, printing, completed, or failed

PrintQuota quota = await client.GetPrintQuotaAsync();
Console.WriteLine(quota.JobsToday.Remaining);
```

- Every source prints, including NiceLabel, BarTender, and DYMO files, except a URL and base64 text.
  Fetch or decode the document and print its bytes. `ToPrinter` throws
  `LabelZoomValidationException` for either one.
- The printer target offers `WithRotation`. The other target options depend on the output language,
  which the printer sets.
- `WithCopies(n)` sends the whole job `n` times, from 1 to 9999. It multiplies a quantity in the
  document: `^PQ3` with 5 copies prints 15 labels.
- `WithIdempotencyKey` makes a print safe to repeat: a second request with the same key returns the
  original job, with `Idempotent` set, instead of printing again. Use a value unique to the label,
  such as an order number. Without a key, the SDK generates one per call and reuses it on its own
  retries. Keys are scoped to your account, so a deliberate reprint needs a new key.
- A template takes merge data instead of source options, one label per record. `WithValidate`
  checks every record for missing merge fields before anything prints, and a missing field throws
  `LabelZoomUnprocessableException`. `quota.Limits.MaxBatchSize` is the most records one request
  can send.
- In a template batch, the server gives each record its own key derived from yours, so a repeated
  batch prints only the records that didn't print before. If `batch.Remaining` isn't 0, the request
  ran out of time: send the same request with the same key to submit the rest.
- `PrintJob.Status` is `dispatched` when an online agent took the job, or `queued` when none is
  online. Statuses are strings, so a new status doesn't break your code.
- The daily job limit throws `LabelZoomPrintQuotaExceededException`, which the SDK never retries.
  The count resets at 00:00 UTC, at `quota.JobsToday.ResetAt`.

## Formats

Sources: `Zpl` `Epl` `Ipl` `Tspl` `Dpl` `Sbpl` `EscLabel` `Fingerprint` `Ezpl` `Cpcl` `Tpcl` `Nlbl`
`Lbl` `Btw` `Dymo` `Xml` `Json` `Pdf` `Png` `Bmp` `Gif` `Jpeg` `Jpg` (an alias for `Jpeg`) `Url`

Targets: `Zpl` `Epl` `Ipl` `Tspl` `Dpl` `Sbpl` `Cpcl` `Xml` `Json` `Pdf` `Png` `Bmp` `Gif` `Jpeg`

| Format | Notes |
|---|---|
| ESC/Label, Fingerprint, EZPL, TPCL | Source only. |
| NiceLabel (`Nlbl` for `.nlbl`, `Lbl` for `.lbl`), BarTender (`Btw` for `.btw` and `.btc`), DYMO | Source only. Send NiceLabel and BarTender files as bytes or a stream, never as a decoded string. |
| ESC/Label | A dialect of ZPL, so `FromEscLabel` offers the ZPL source options. The job declares its own resolution with `^S(CLR,R` or `^MU`, and `WithDpi` overrides it. A job with `~DY` carries raw PNG bytes, so pass it as bytes. |
| CPCL | Source and target. The job's header declares its resolution, and `WithDpi` overrides it. Output concatenates every label and writes graphics as hex, so it is plain text. |
| EPL, IPL, TSPL, DPL, SBPL | Source and target. Output is `text/plain` with every label concatenated, but it can carry raw binary (EPL's `GW`, TSPL's `BITMAP`, IPL's `STX`/`ETX`-framed bitmap columns, SBPL's inline graphics). Read `result.Bytes`, not `result.Text`, whenever a label might carry graphics. |
| ZPL | Output concatenates every label. |
| PDF | Output has one page per label. |
| XML, PNG, BMP, GIF, JPEG | Output holds the first label only. |
| JSON | Output holds the first label only, and needs a paid plan. |
| `Url` | Source only. The server fetches the URL you supply and converts what it finds there. Validate the URL first if it came from untrusted input. |

`SourceFormat` and `TargetFormat` are distinct types, so `.ToUrl()` doesn't exist and
`To(SourceFormat.Pdf)` doesn't compile. `Jpg` is an input spelling that is sent as `jpeg`.

## Options by stage

### Source options

Set these after `From*` and before `To*`.

| Method | Available on | What it does |
|---|---|---|
| `WithLabelSize(float, float)` | Every source | Label width and height in inches, not dots. Replaces the size the source declares; omit it to use the declared size, or a size detected from the content. |
| `WithLabelIndex(int)` | Every source | Converts one label of a multi-label source, on every plan. 0-based. For an image or PDF, it counts labels after any split, page by page. |
| `WithData(params object?[])` | Every source | Fills variable fields. Each record produces one label. |
| `WithDpi(int)` | Printer-language and image sources | The resolution the job or image was made for. The server default is 203. |
| `WithPage(int)` | `FromPdf` | Converts one page. 0-based; omit it to convert every page. |
| `WithConversionMode(PdfConversionMode)` | `FromPdf` | `Image`, the default, rasterizes the page; `Native` reads its drawing operations. Split and layout need `Image`. |
| `WithCommandsToIgnore(params string[])` | `FromZpl`, `FromEscLabel` | ZPL commands the parser skips, for example `"^PQ"`. |
| `WithSplit(s => ...)` | `FromPdf`, `FromPng`, `FromBmp`, `FromGif`, `FromJpeg` | Cuts each page or image into a grid of labels, read left to right and then down. ZPL target in `Image` mode only. |
| `WithRaster(r => ...)` | `FromPdf`, `FromPng`, `FromBmp`, `FromGif`, `FromJpeg` | How the picture is rasterized. In `Native` mode, only `Scaling` applies. |

| `WithSplit` field | What it does |
|---|---|
| `Rows(int)`, `Columns(int)` | The grid. |
| `LabelSize(float, float)` | Each label's width and height, in inches. Unset, the page divides equally. |
| `Margins(float, float)` | Left and top distance to the first label, in inches. |
| `Gaps(float, float)` | Column and row gaps, in inches. |
| `SkipBlank(bool)` | Leaves out labels with nothing printable. |

| `WithRaster` field | What it does |
|---|---|
| `Scaling(float)` | Scale, in percent. The server default is 100. |
| `Position(int, int)` | Where the picture sits on the label, in output dots from its top-left corner. A negative offset crops. Printer-language targets only. |
| `Darkness(int)` | Black-and-white threshold, 0 to 100. The server default is 70. It also decides which labels `SkipBlank` treats as blank. |
| `ColorMode(ColorMode)` | How color is reduced: `Bw`, `Grayscale` (the default, which dithers), or `Color`. |

### Target options

Set these after `To*` and before `ExecuteAsync`.

| Method | Available on | What it does |
|---|---|---|
| `WithDpi(int)` | Every target but `ToPdf` | The resolution of the output. The server default is the source's resolution, else 203. |
| `WithRotation(int)` | Every target, and `ToPrinter` | Degrees clockwise. Must be a multiple of 90, or the SDK throws `LabelZoomValidationException`. |
| `WithWatermark(bool)` | Every target | Requests a watermark. The free tier watermarks output regardless. |
| `WithDialect(string)` | Every target | A printer dialect, for example `moca`. Needs a paid plan. |
| `WithParameter(string, object?)` | Every target | Sets a parameter the SDK doesn't model. The server ignores keys it doesn't know. |
| `WithRawQueryParameter(string, string)` | Every target | Adds a query-string parameter beside `params`. |
| `WithImageCompression(ZplImageCompression)` | `ToZpl` | `Z64`, the default, or `CompressedHex`. |
| `WithCollapseCopies(bool)` | `ToZpl` | Image or PDF source: each run of identical labels becomes one label with a `^PQ` count. |
| `WithLayout(l => ...)` | `ToZpl` | Image or PDF source in `Image` mode: labels side by side in each row, for multi-column rolls. `Across(int)` (1 to 10), `Gap(float)` in inches, and `Fill(LayoutFill)`: `Sequential` (the default) or `Repeat`, for a single image or a one-page file. |
| `WithColorMode(ColorMode)`, `WithDarkness(int)` | `ToPng`, `ToBmp`, `ToGif`, `ToJpeg` | `Bw` renders a black-and-white image at the darkness threshold. Any other mode renders in color, the default. |

With `WithLayout`, the label size stays the size of one label, not the whole row.

### Choose formats at run time

`From(SourceFormat, ...)`, `FromFile`, `FromBase64Text`, and `FromUrl` return a `DynamicSourceBuilder`,
and `To(TargetFormat)` returns a `DynamicTargetBuilder`. The compiler can't know the format there, so
they offer every option, named with the format it applies to:

| Stage | Options |
|---|---|
| `DynamicSourceBuilder` | `WithDpi`, `WithPdfPage`, `WithPdfConversionMode`, `WithZplCommandsToIgnore`, `WithSplit`, `WithRaster`, and the options for every source |
| `DynamicTargetBuilder` | `WithDpi`, `WithZplImageCompression`, `WithZplCollapseCopies`, `WithLayout`, `WithColorMode`, `WithDarkness`, and the options for every target |

The server ignores an option that doesn't apply to the format. `WithColorMode` and `WithDarkness` on
the target stage set the same values as `WithRaster` on the source stage, so set each one once.

```csharp
var result = await client.Convert()
    .FromFile(SourceFormat.Pdf, "shipping-label.pdf")
        .WithPdfPage(0)
    .To(TargetFormat.Zpl)
    .ExecuteAsync();
```

## Errors

Every non-2xx response throws a subclass of `LabelZoomException`:

| Exception | Status |
|---|---|
| `LabelZoomBadRequestException` | 400 |
| `LabelZoomUnauthorizedException` | 401 |
| `LabelZoomForbiddenException` | 403. `IsPaidFeature` is `true` when the request needs a paid plan. |
| `LabelZoomNotFoundException` | 404 |
| `LabelZoomPayloadTooLargeException` | 413 |
| `LabelZoomUnprocessableException` | 422 |
| `LabelZoomRateLimitedException` | 429. `RetryAfterSeconds` holds the parsed `Retry-After`. |
| `LabelZoomPrintQuotaExceededException` | 429 for the daily print job limit. It derives from `LabelZoomRateLimitedException`. |
| `LabelZoomServerException` | 5xx |
| `LabelZoomException` | Any other status |

Each one carries `StatusCode`, `Message` (the server's message), `RawBody`, `RequestId` (the
`X-LZ-Request-Id` to quote to support), and `Code`. `Code` is the error's stable code, such as
`agent_update_required` or `idempotency_key_reused`, or `null`. Branch on it rather than on the
message.

```csharp
try
{
    var result = await client.Convert().FromZpl(zpl).ToJson().ExecuteAsync();
}
catch (LabelZoomForbiddenException ex) when (ex.IsPaidFeature)
{
    // "JSON export is a paid feature": the most common free-tier failure.
    Console.WriteLine($"{ex.Message} (request {ex.RequestId})");
}
catch (LabelZoomException ex)
{
    Console.WriteLine($"{(int)ex.StatusCode} {ex.Code}: {ex.Message}");
}
```

`LabelZoomValidationException` means the calling code is wrong, such as a rotation of 45 or an empty
document. The SDK throws it before any request is sent. It derives from `ArgumentException`, not
`LabelZoomException`, so a handler for server failures doesn't catch it, and `ParamName` names the
argument.

## Retries

The SDK retries `429`, `5xx`, and transport failures, including an `HttpClient` timeout: 3 attempts
in total, with backoff of 1, 2, and 4 seconds and full jitter. When `Retry-After` asks for a longer
wait, the SDK waits that long. It returns other `4xx` responses at once, and never retries the daily
print limit. Cancellation through your `CancellationToken` stops the call without a retry.

To change the policy, set `MaxRetries`, the number of retries after the first attempt:

```csharp
using var client = new LabelZoomClient(new LabelZoomClientOptions
{
    MaxRetries = 0,                                 // disables retries; the default is 2
    Timeout = TimeSpan.FromSeconds(30),
});
```

## Configure the client

| `LabelZoomClientOptions` property | What it does |
|---|---|
| `ApiKey` | The API key or JWT. `null` reads `LABELZOOM_API_KEY`; an empty string forces anonymous mode. |
| `BaseUrl` | The API base URL. The default is `https://api.labelzoom.com`. A path prefix is kept, so a reverse proxy at `https://proxy.example.com/labelzoom` works. |
| `Timeout` | The per-request timeout. The default is 100 seconds. Ignored when you supply `HttpClient`. |
| `MaxRetries` | Retries after the first attempt. The default is 2; 0 disables retries. |
| `HttpClient` | An `HttpClient` to use. You own its lifetime, and the SDK doesn't dispose it. |
| `HttpMessageHandler` | A handler for the `HttpClient` the SDK creates. Ignored when you supply `HttpClient`. |
| `UserAgentSuffix` | Text appended to the SDK's `User-Agent`, to identify your application. |
| `UseJitter` | Whether retry backoff has jitter. The default is `true`. |
| `SleepAsync` | Replaces the delay between retries. |

### Register the client for dependency injection

```csharp
services.AddHttpClient("labelzoom");
services.AddSingleton(sp => new LabelZoomClient(new LabelZoomClientOptions
{
    HttpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient("labelzoom"),
}));
```

### Test your own code

To stub the API in your tests, set `HttpMessageHandler`. To test retry behavior without waiting, turn
off jitter and record the delays instead of sleeping:

```csharp
var options = new LabelZoomClientOptions
{
    HttpMessageHandler = yourStubHandler,
    UseJitter = false,
    SleepAsync = (delay, _) => { recorded.Add(delay); return Task.CompletedTask; },
};
```

## Migrate from 1.x

In 1.x every option was set after `To*`. Move each source option before `To*`:

| 1.x | 2.0 |
|---|---|
| `.ToZpl().WithPdfPage(0)` | `.FromPdf(pdf).WithPage(0).ToZpl()` |
| `.WithPdfConversionMode(m)` | `.FromPdf(pdf).WithConversionMode(m)` |
| `.WithZplCommandsToIgnore("^PQ")` | `.FromZpl(zpl).WithCommandsToIgnore("^PQ")` |
| `.WithLabelSize(w, h)`, `.WithLabelIndex(i)`, `.WithData(...)` | The same methods, before `To*` |
| `.WithZplImageCompression(c)` | `.ToZpl().WithImageCompression(c)` |
| `.WithDpi(d)`, which set both resolutions | `.WithDpi(d)` on the target stage for the output. Add `.WithDpi(d)` on the source stage too if the job was made for `d`. Remove it for a PDF target. |
| `.WithScaling(p)`, `.WithPosition(x, y)` | `.FromPdf(pdf).WithRaster(r => r.Scaling(p).Position(x, y))`, or the same on `FromPng` and the other image sources |
| `.WithDarkness(d)`, `.WithColorMode(m)` | `WithRaster(r => r.Darkness(d).ColorMode(m))` for an image or PDF source; `.ToPng().WithColorMode(m).WithDarkness(d)` and the other image targets for black-and-white output |
| `ConversionSourceBuilder`, `ConversionTargetBuilder` | `SourceStage<T>` and `TargetStage<T>` subclasses, such as `PdfSourceBuilder` and `ZplTargetBuilder` |
| No print support | `.ToPrinter(printerId)`, `.FromTemplate(templateId)`, `client.Jobs.GetAsync`, and `client.GetPrintQuotaAsync` |

`FromFile(SourceFormat.Pdf, path)` still works. It returns a `DynamicSourceBuilder`, which keeps the
format-prefixed names such as `WithPdfPage`.

## Contributing

Run the tests from the `dotnet` directory:

```sh
dotnet test --filter "Category!=Integration"    # offline: no key, no network
dotnet test --filter "Category=Integration"     # calls the real API with LABELZOOM_API_KEY
```

The offline suite runs the shared [conformance fixtures](../conformance/) that every LabelZoom SDK is
checked against, and asserts that it ran all of them. See [docs/CONFORMANCE.md](../docs/CONFORMANCE.md).
The build treats compiler and documentation warnings as errors, so a clean `dotnet build` is the lint
check.

## License

MIT. See [LICENSE](../LICENSE).
