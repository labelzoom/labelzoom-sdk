![LabelZoom Logo](../docs/LabelZoom_Logo_f_400px.png)

# LabelZoom Go SDK

The LabelZoom Go SDK converts barcode labels through the [LabelZoom API](https://api.labelzoom.com)
and prints them with Cloud Print. It converts between ZPL, EPL, IPL, TSPL, DPL, SBPL, CPCL, PDF,
LabelZoom XML and JSON, and raster images, and it reads ESC/Label, Fingerprint, EZPL, TPCL,
NiceLabel, BarTender, and DYMO files.

## What the SDK handles

- Retries `429`, `5xx`, and network failures, and waits for a `Retry-After` that's longer than its
  own backoff.
- Returns a typed error for every non-2xx response, with the status, message, error `code`, and
  request ID.
- Returns every result as bytes, so binary output such as PDF, PNG, or EPL with graphics isn't
  corrupted.
- Sends an `Idempotency-Key` with every print, so a retried print doesn't print twice.
- Works without an API key, on the watermarked free tier. See
  [Rate limits and plans](https://docs.labelzoom.com/reference/rate-limits-and-plans/).
- Uses only the Go standard library.

## Install

The SDK requires Go 1.23 or later.

1. Add the module to your project:

   ```sh
   go get github.com/labelzoom/labelzoom-sdk/go/v2
   ```

2. Import the package:

   ```go
   import labelzoom "github.com/labelzoom/labelzoom-sdk/go/v2"
   ```

The module lives in the `go/` directory of a multi-language repository, so its path ends in
`/go/v2` and its release tags are `go/vX.Y.Z`.

### Set an API key

An API key is optional. Without one, the client uses the free tier: watermarked output, one label
per request (the first, or the one `LabelIndex` selects), and no multi-page, JSON-target, or
image-to-image conversion.

| Call | Credential |
| --- | --- |
| `labelzoom.New()` | Reads `LABELZOOM_API_KEY`. If it's unset or empty, the client uses the free tier. |
| `labelzoom.New(labelzoom.WithAPIKey("lz_live_..."))` | Uses the key or JWT you pass. |
| `labelzoom.New(labelzoom.WithAnonymous())` or `WithAPIKey("")` | Uses the free tier and ignores `LABELZOOM_API_KEY`. |

`client.IsAuthenticated()` reports whether the client found a credential.

## Convert a label

A conversion reads a source document and writes it in a target format. The following program
converts the first page of a PDF to ZPL for a 203 dpi printer:

```go
package main

import (
	"context"
	"log"
	"os"

	labelzoom "github.com/labelzoom/labelzoom-sdk/go/v2"
)

func main() {
	client, err := labelzoom.New() // reads LABELZOOM_API_KEY
	if err != nil {
		log.Fatal(err)
	}

	pdf, err := os.ReadFile("shipping-label.pdf")
	if err != nil {
		log.Fatal(err)
	}

	result, err := client.Convert(context.Background(), labelzoom.ConvertRequest{
		From: labelzoom.SourcePDF,
		Body: pdf,
		To:   labelzoom.TargetZPL,
		Options: &labelzoom.Options{
			// Source options: how the PDF is read.
			PDF:   &labelzoom.PDFOptions{PageNumber: labelzoom.Ptr(0)}, // 0-based
			Label: &labelzoom.LabelSize{Width: labelzoom.Ptr(4.0), Height: labelzoom.Ptr(6.0)},
			// Target options: what's written.
			TargetDPI: labelzoom.Ptr(203),
		},
	})
	if err != nil {
		log.Fatal(err)
	}

	if err := result.Save("label.zpl"); err != nil {
		log.Fatal(err)
	}
}
```

`Options` holds two kinds of option. A source option changes how the document is read, such as the
label size in inches, the PDF page, or the merge data. A target option changes what's written, such
as the output density, the rotation, or the ZPL image compression. Each option acts only on the
formats listed in [Options by stage](#options-by-stage). For example, `SourceDPI` on a PDF source
and `TargetDPI` on a PDF target both return `400`.

Every field of `Options` is a pointer, and the SDK sends only the fields you set. It never sends a
default of its own, so `Watermark: labelzoom.Ptr(false)` and `PageNumber: labelzoom.Ptr(0)` are
sent, and an omitted field isn't. Build the pointers with `labelzoom.Ptr`. A float field needs a
float literal: write `labelzoom.Ptr(4.0)`, not `labelzoom.Ptr(4)`.

`Convert` returns a `*labelzoom.Result`:

| Member | Contains |
| --- | --- |
| `Bytes` | The response body, exactly as the server sent it. |
| `Text()` | `Bytes` decoded with the response charset, UTF-8 by default. |
| `Save(path)` | Writes `Bytes` to a file. |
| `ContentType` | The response `Content-Type`. |
| `Status` | The HTTP status code. |
| `RequestID` | The `X-LZ-Request-Id` support handle, or `""`. |

Read `Bytes`, not `Text()`, for a binary target and for any printer-language target that might
carry graphics.

To send a PDF or image as base64 text instead of its own bytes, set `AsBase64Text: true` on the
`ConvertRequest`. Only the PDF and image sources take it.

## Print a label

Cloud Print sends a label to a printer that a LabelZoom Print Agent serves. Printing needs an API
key with the `print` scope. A printer is a target: the printer's configuration picks the output
language, and LabelZoom converts the document to it, so a PDF prints on a ZPL printer.

### Print a document

```go
job, err := client.Print(ctx, labelzoom.PrintRequest{
	Source:         labelzoom.SourcePDF,
	Body:           pdf,
	Options:        &labelzoom.PrintOptions{PDF: &labelzoom.PDFOptions{PageNumber: labelzoom.Ptr(0)}},
	PrinterID:      printerID,
	Copies:         labelzoom.Ptr(2),
	IdempotencyKey: "SO-1001",
})
if err != nil {
	return err
}
log.Printf("job %s is %s", job.JobID, job.Status)
```

`Print` returns a `*labelzoom.PrintJob` with `JobID`, `Status` (`dispatched` when an online agent
took the job, `queued` when none is online), `Copies`, and `Idempotent`.

| Field | Rule |
| --- | --- |
| `Source` | Every source format prints except `SourceURL`, which returns a `*ValidationError` with `Parameter` set to `source`. Fetch the document and print its bytes. |
| `Body` | The format's own bytes. Print takes no base64 text. |
| `Options` | A `*PrintOptions`: the source options of `Options`, with the same meaning, plus `Rotation`. It has no other target option, because the printer picks the output language. `PrintOptions.ZPL` is a `ZPLSourceOptions`, which holds `CommandsToIgnore`. Nil sends a document that's already in the printer's language as is. |
| `Copies` | Sends the whole job 1 to 9999 times, and multiplies any quantity the document sets itself: `^PQ3` with 3 copies prints 9 labels. Nil leaves it to the document. |
| `IdempotencyKey` | The same key returns the original job, with `Idempotent` set to `true`, instead of printing again. Use a value unique to the label, such as an order number. Empty has the SDK generate one per call, which its own retries reuse. |

### Print a template

A template is a label published from LabelZoom Studio or the web designer. `PrintTemplate` prints
one label per record:

```go
batch, err := client.PrintTemplate(ctx, labelzoom.TemplatePrintRequest{
	TemplateID:     templateID,
	Data:           []labelzoom.DataRecord{{"sku": "A-100"}, {"sku": "B-200"}},
	Validate:       true,
	PrinterID:      printerID,
	Copies:         labelzoom.Ptr(2),
	IdempotencyKey: "SO-1001-labels",
})
if err != nil {
	return err
}
for _, job := range batch.Jobs {
	log.Printf("job %s is %s", job.JobID, job.Status)
}
```

- `Data` takes a `DataRecord` for one label or a `[]DataRecord` for several. Nil prints a template
  with no merge fields.
- `Validate` checks every record for missing merge fields before anything prints.
- `Copies` prints each label 1 to 9999 times.
- `IdempotencyKey` is one key for the whole batch.
- `PrintTemplate` always returns a `*labelzoom.PrintBatch`. If `batch.Remaining` isn't 0, the last
  `Remaining` records weren't submitted, and `batch.Message` says why. To submit them, send the
  same request with the same `IdempotencyKey`. The records already submitted don't print again.

### Follow a job

```go
details, err := client.GetPrintJob(ctx, job.JobID)
if err != nil {
	return err
}
log.Printf("job %s is %s", details.ID, details.Status)
```

`details.Status` is `queued`, `dispatched`, `printing`, `completed`, or `failed`, and
`details.Error` says why a job failed. The set of statuses can grow, so `Status` is a `string`.
The timestamps (`CreatedDate`, `DispatchedDate`, `CompletedDate`, and `RetriedDate`) are nil until
the job reaches that point.

### Check the print quota

```go
quota, err := client.GetPrintQuota(ctx)
if err != nil {
	return err
}
log.Printf("%d of %d jobs left today", quota.JobsToday.Remaining, quota.JobsToday.Limit)
```

`GetPrintQuota` returns the plan, the `Agents`, `Printers`, and `JobsToday` counters, and the
request size `Limits`. When the daily job limit is used up, a print returns a
`*PrintQuotaExceededError`, which also matches `*RateLimitedError`. The client doesn't retry it,
because the limit resets at 00:00 UTC (`quota.JobsToday.ResetAt`).

## Formats

Sources:

`SourceZPL` `SourceEPL` `SourceIPL` `SourceTSPL` `SourceDPL` `SourceSBPL` `SourceESCLabel`
`SourceFingerprint` `SourceEZPL` `SourceCPCL` `SourceTPCL` `SourceNLBL` `SourceLBL` `SourceBTW`
`SourceDYMO` `SourceXML` `SourceJSON` `SourcePDF` `SourcePNG` `SourceBMP` `SourceGIF` `SourceJPEG`
`SourceJPG` `SourceURL`

Targets:

`TargetZPL` `TargetEPL` `TargetIPL` `TargetTSPL` `TargetDPL` `TargetSBPL` `TargetCPCL` `TargetXML`
`TargetJSON` `TargetPDF` `TargetPNG` `TargetBMP` `TargetGIF` `TargetJPEG`

`labelzoom.SourceFormats()` and `labelzoom.TargetFormats()` list them.

- ESC/Label, Fingerprint, EZPL, TPCL, NiceLabel (`SourceNLBL`, `SourceLBL`), BarTender
  (`SourceBTW`), and DYMO are source-only. Send NiceLabel and BarTender files as bytes.
- `SourceFormat` and `TargetFormat` are distinct types, so `To: labelzoom.SourceJPG` doesn't
  compile. `SourceJPG` is sent as `jpeg`.
- `SourceURL` has the server fetch a document and convert it. The body is the URL, as text. If the
  URL comes from untrusted input, validate it first: the server fetches it from LabelZoom's network.
- The printer languages are sources and targets: `pdf` to `epl` and `zpl` to `tspl` are real
  conversions. Their output is `text/plain`, but EPL (`GW`), TSPL (`BITMAP`), IPL, and SBPL can
  inline raw binary graphics, so read `result.Bytes`, not `result.Text()`. CPCL writes graphics as
  hex, so its output is plain text.

## Options by stage

### Source options

| Field | What it does | Formats |
| --- | --- | --- |
| `SourceDPI` | The density the source was made for, which sets how its dots or pixels map to inches. Server default 203. A value below 1 returns a `*ValidationError`. | Printer-language and image sources. A PDF, NiceLabel, BarTender, XML, or JSON source returns `400`. |
| `Label` (`LabelSize`) | The media size, in inches. Set `Width`, `Height`, or both. An axis you leave nil comes from the size the code declares, else from the content. Omit `Label` to detect both. | Every source |
| `LabelIndex` | The 0-based index of the one label to convert. Omit it to convert every label. For an image or PDF to a printer language, it counts the labels after `Split`, page by page. Out of range returns `404`. | Every source |
| `Data` | The variable-field values, one record per output label. A single `DataRecord` is sent as one record. An element that isn't an object returns a `*ValidationError`. | Every source |
| `PDF.PageNumber` | The 0-based page to convert. Omit it to convert every page. | PDF |
| `PDF.ConversionMode` | `PDFConversionModeImage` rasterizes the page (server default). `PDFConversionModeNative` extracts its text and vectors. | PDF |
| `ZPL.CommandsToIgnore` | Skips the named commands while reading, such as `[]string{"^PQ"}`. | ZPL, ESC/Label |
| `Split` (`SplitOptions`) | Cuts each page into a grid of `Rows` by `Columns` labels, 1 to 20 each and at most 100 in all, read left to right and then down. Every length is in inches on the page. Leave `Width` and `Height` nil to divide the page equally. `SkipBlank` leaves out empty labels. | Image or PDF to a printer language |
| `Scaling` | The size the picture is rendered at, as a percentage. Server default 100. | Image, PDF |
| `Position` | The picture's `X` and `Y` offset on the label. A negative offset crops it. | Image, PDF |
| `ColorMode` | `ColorModeBW`, `ColorModeGrayscale` (server default), or `ColorModeColor`. | Image, PDF; also a target option |
| `Darkness` | The black-and-white threshold, 0 to 100. Server default 70. A value outside that range returns a `*ValidationError`. | Image, PDF; also a target option |

### Target options

| Field | What it does | Formats |
| --- | --- | --- |
| `TargetDPI` | The density of the output. Server default `SourceDPI`, else 203. A value below 1 returns a `*ValidationError`. | Every target except PDF, which returns `400` |
| `Rotation` | Turns the finished label, in degrees clockwise. Server default 0. A value that isn't a multiple of 90 returns a `*ValidationError`. | Every target |
| `Watermark` | Adds a watermark. The free tier is always watermarked. | Every target |
| `Dialect` | An output dialect, such as `"moca"`. Paid plans only. | ZPL |
| `ColorMode` | With `ColorModeBW`, returns a black-and-white image at the `Darkness` threshold. | PNG, BMP, GIF, JPEG |
| `Darkness` | The threshold for `ColorModeBW`. | PNG, BMP, GIF, JPEG |
| `ZPL.ImageCompression` | `ZPLImageCompressionZ64` (server default) or `ZPLImageCompressionCompressedHex`, for older printer firmware. | ZPL |
| `ZPL.CollapseCopies` | Sends each run of identical labels as one label with a `^PQ` count. | Image or PDF to ZPL |
| `Layout` (`LayoutOptions`) | Puts `Across` labels, 1 to 10, side by side in each row, `Gap` inches apart, for multi-column label stock. `Fill` is `LayoutFillSequential` (server default), which puts each page in the next column, or `LayoutFillRepeat`, which fills a whole row with each page. | Image or PDF to a printer language |

`ColorMode` and `Darkness` are one field each, so a conversion from an image to an image uses the
same value for both stages.

`Split`, `ZPL.CollapseCopies`, and `Layout` need a PDF in `PDFConversionModeImage`, the default.
With `PDFConversionModeNative`, `Across` above 1 or a split grid returns `400`, and
`CollapseCopies` has no effect.

`Options.Extra` carries options the SDK doesn't model yet. Its keys are merged at the top level of
the `params` JSON and override the fields above, and the server ignores keys it doesn't know.
`Options.RawQuery` adds query parameters beside `params`. Prefer `Extra`.

## Errors

Every non-2xx response returns a typed error. Each type embeds `labelzoom.APIError` and unwraps to
`*labelzoom.APIError`, so one `errors.As` catches them all:

| Status | Type | Adds |
| --- | --- | --- |
| `400` | `*BadRequestError` | |
| `401` | `*UnauthorizedError` | |
| `403` | `*ForbiddenError` | `IsPaidFeature`, true when the request needs a paid feature rather than other permissions |
| `404` | `*NotFoundError` | |
| `413` | `*PayloadTooLargeError` | |
| `422` | `*UnprocessableError` | |
| `429` | `*RateLimitedError` | `RetryAfterSeconds`, from the `Retry-After` header |
| `429` with code `daily_job_limit_reached` | `*PrintQuotaExceededError` | Also matches `*RateLimitedError` |
| `5xx` | `*ServerError` | |
| Any other | `*APIError` | |

`APIError` carries `Status`, `Message` (up to 512 characters), `RequestID` (the `X-LZ-Request-Id`
support handle), `RawBody` (the whole response body), and `Code`. `Code` is the error body's
`code`, such as `agent_update_required` or `idempotency_key_reused`, or `""` when there's none.
Branch on `Code`, not on `Message`.

```go
_, err := client.Convert(ctx, request)

var forbidden *labelzoom.ForbiddenError
if errors.As(err, &forbidden) && forbidden.IsPaidFeature {
	// The request needs a paid feature: add an API key or upgrade the plan.
}

var apiErr *labelzoom.APIError
if errors.As(err, &apiErr) {
	log.Printf("request %s failed with %d (%s): %s",
		apiErr.RequestID, apiErr.Status, apiErr.Code, apiErr.Message)
}

var invalid *labelzoom.ValidationError
if errors.As(err, &invalid) {
	log.Printf("fix %s: %s", invalid.Parameter, invalid.Message)
}
```

A `*ValidationError` reports a request the SDK rejected before sending it, which is a bug in the
calling code. It doesn't unwrap to `*APIError`, so a fallback written for `*APIError` doesn't catch
it. `errors.Is(err, labelzoom.ErrValidation)` matches any `*ValidationError`, and its `Parameter`
names the parameter at fault as it appears on the wire.

## Retries

The client retries `429`, `5xx`, and network failures, for 3 attempts in total, and waits up to 1
second, then up to 2 seconds, with full jitter (a third retry waits up to 4 seconds). When a
`Retry-After` header asks for longer, the client waits for the `Retry-After`. It never retries any
other `4xx`, the daily print limit, or a canceled or expired context.

```go
client, err := labelzoom.New(
	labelzoom.WithMaxRetries(4),           // 5 attempts in total
	labelzoom.WithTimeout(30*time.Second), // per attempt
)
```

To turn retries off, use `labelzoom.WithMaxRetries(0)`.

## Configure the client

`labelzoom.New` takes these options. A `*labelzoom.Client` is safe for concurrent use.

| Option | Effect |
| --- | --- |
| `WithAPIKey(key)` | Sets the credential: an `lz_live_` or `lz_test_` key, or a JWT. `""` forces the free tier. |
| `WithAnonymous()` | Forces the free tier and ignores `LABELZOOM_API_KEY`. |
| `WithBaseURL(url)` | Overrides `https://api.labelzoom.com`. A path prefix is kept, so a reverse proxy at `https://proxy.example.com/labelzoom` works. |
| `WithMaxRetries(n)` | Retries after the first attempt. Default 2. `0` turns retries off, and a negative value makes `New` return an error. |
| `WithTimeout(d)` | A timeout for each attempt. Default none, beyond the context's and the `http.Client`'s. |
| `WithUserAgentSuffix(s)` | Appends `s` to the SDK's `User-Agent`. |
| `WithHTTPClient(c)` | Sends every request through `c`. |
| `WithSleeper(f)` | Replaces the wait between retries. |
| `WithoutJitter()` | Makes the backoff exactly 1, 2, and 4 seconds. |
| `WithEnvLookup(f)` | Replaces the lookup that reads `LABELZOOM_API_KEY`. |

## Test your own code

To test code that calls the SDK without opening a socket or sleeping, give the client a stub
transport and a recording sleeper:

```go
type roundTripperFunc func(*http.Request) (*http.Response, error)

func (f roundTripperFunc) RoundTrip(r *http.Request) (*http.Response, error) { return f(r) }

func newTestClient() (*labelzoom.Client, *[]time.Duration, error) {
	stub := func(r *http.Request) (*http.Response, error) {
		return &http.Response{
			StatusCode: 200,
			Header:     http.Header{"Content-Type": {"text/plain"}},
			Body:       io.NopCloser(strings.NewReader("^XA^XZ")),
		}, nil
	}

	var slept []time.Duration
	client, err := labelzoom.New(
		labelzoom.WithHTTPClient(&http.Client{Transport: roundTripperFunc(stub)}),
		labelzoom.WithSleeper(func(d time.Duration) { slept = append(slept, d) }),
		labelzoom.WithoutJitter(),
		labelzoom.WithEnvLookup(func(string) (string, bool) { return "", false }),
	)
	return client, &slept, err
}
```

Use `WithEnvLookup` even when a test doesn't care about credentials. Without it, a developer's
`LABELZOOM_API_KEY` changes what the SDK sends.

## Migrate from 1.x

| 1.x | 2.0 |
| --- | --- |
| `go get github.com/labelzoom/labelzoom-sdk/go` | `go get github.com/labelzoom/labelzoom-sdk/go/v2`, and change every import to `github.com/labelzoom/labelzoom-sdk/go/v2` |
| `Options.DPI` on a printer-language or image source | `Options.SourceDPI` for the density the source was made for, and `Options.TargetDPI` for the output. Set both to the 1.x value to get the 1.x output. |
| `Options.DPI` on a PDF source | `Options.TargetDPI` |
| `Options.DPI` with a PDF target | Remove it. A PDF target has no density. |
| A `422` returned as `*APIError` | `*UnprocessableError`, which unwraps to `*APIError` |
| Matching an error by `Message` | Match `APIError.Code` |
| No print support | `client.Print`, `client.PrintTemplate`, `client.GetPrintJob`, and `client.GetPrintQuota` |

`ConvertRequest`, the other `Options` fields, the format constants, and the client options keep
their 1.x names and meaning.

## Contributing

Run these from the `go/` directory:

```sh
gofmt -l .          # must print nothing
go vet ./...
go build ./...
go test ./... -count=1 -race
```

The test suite runs the shared conformance fixtures in [`../conformance/`](../conformance/), the
same cases every other SDK runs, and asserts that it ran every case except the skips listed, each
with its reason, in `conformance/skips/go.json`. Go runs the `typecheck/*` cases for real: it builds
each snippet in `testdata/typecheck/` and asserts that the compiler rejects it. See
[CONFORMANCE.md](../docs/CONFORMANCE.md) for how the suite works.

The SDK uses functional options for the client and a request struct for each call, rather than the
fluent chain the other SDKs use: a chain in Go has to either panic on a bad argument or defer its
errors to a terminal call. The wire behavior is the same. See
[API_CONTRACT.md section 9](../docs/API_CONTRACT.md#9-divergences).

## License

MIT. See [LICENSE](LICENSE).
