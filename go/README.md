![LabelZoom Logo](../docs/LabelZoom_Logo_f_400px.png)

# LabelZoom Go SDK

Official Go client for the [LabelZoom API](https://api.labelzoom.com). Converts barcode labels
between ZPL, EPL, TSPL, DPL, PDF, LabelZoom XML/JSON, and raster images.

Go 1.23+. **No dependencies** — standard library only, including the response-charset decoding.

## Install

```sh
go get github.com/labelzoom/labelzoom-sdk/go
```

```go
import labelzoom "github.com/labelzoom/labelzoom-sdk/go"
```

The import path ends in `/go` because the module lives in the `go/` subdirectory of a
multi-language repository; its release tags are correspondingly `go/vX.Y.Z`.

## Quick start

**An API key is optional.** Without one you get the free tier — watermarked output, first label
only, a 1 MB request cap, and no multi-page, JSON-target, or image-to-image conversion.

```go
client, err := labelzoom.New()          // anonymous; this works
if err != nil {
    return err
}

result, err := client.Convert(ctx, labelzoom.ConvertRequest{
    From: labelzoom.SourceZPL,
    To:   labelzoom.TargetPNG,
    Body: []byte("^XA^FO20,20^A0N,28^FDHello^FS^XZ"),
    Options: &labelzoom.Options{
        DPI:   labelzoom.Ptr(300),
        Label: &labelzoom.LabelSize{Width: labelzoom.Ptr(4.0), Height: labelzoom.Ptr(6.0)},
    },
})
if err != nil {
    return err
}

return result.Save("label.png")
```

With a credential:

```go
client, err := labelzoom.New(labelzoom.WithAPIKey("lz_live_..."))
```

Passing nothing reads `LABELZOOM_API_KEY` from the environment. Passing `WithAPIKey("")` — or
the more readable `WithAnonymous()` — forces the free tier and suppresses that fallback.

### Why a request struct and not a fluent chain

The other LabelZoom SDKs expose `client.convert().fromZpl(body).toPng().withDpi(300).execute()`.
A chain in Go has to either panic on a bad argument or hoard errors until a terminal `Do()`, and
both are un-Go, so this SDK uses functional options for the client and a request struct for the
call. The wire behaviour is identical — that is what the shared conformance suite proves. See
[API_CONTRACT.md §9](../docs/API_CONTRACT.md#9-divergences).

## Formats

**Sources (13):** `SourceZPL` `SourceEPL` `SourceTSPL` `SourceDPL` `SourceXML` `SourceJSON`
`SourcePDF` `SourcePNG` `SourceBMP` `SourceGIF` `SourceJPEG` `SourceJPG` `SourceURL`

**Targets (11):** `TargetZPL` `TargetEPL` `TargetTSPL` `TargetDPL` `TargetXML` `TargetJSON`
`TargetPDF` `TargetPNG` `TargetBMP` `TargetGIF` `TargetJPEG`

`SourceFormat` and `TargetFormat` are distinct types. `SourceJPG` is an input spelling that
normalizes to `jpeg` on the wire, and `SourceURL` tells the server to go fetch a document rather
than naming a format — so neither has a `TargetFormat` counterpart, and
`To: labelzoom.SourceJPG` does not compile.

The printer languages round-trip: `pdf`→`epl` and `zpl`→`tspl` are real conversions. Their
output is `text/plain`, but EPL's `GW` and TSPL's `BITMAP` commands inline raw binary, so read
`result.Bytes` rather than `result.Text()` whenever a label might carry graphics.

## Options

Every field of `Options` is a pointer, and **only the ones you set are sent** — the SDK never
substitutes a default of its own, so a change to a server default reaches you without an SDK
upgrade. `labelzoom.Ptr` is the constructor:

```go
Options: &labelzoom.Options{
    DPI:       labelzoom.Ptr(300),          // server default 203
    Rotation:  labelzoom.Ptr(90),           // must be a multiple of 90
    Scaling:   labelzoom.Ptr(75.0),         // percent; server default 100
    ColorMode: labelzoom.Ptr(labelzoom.ColorModeGrayscale),
    Darkness:  labelzoom.Ptr(60),           // 0-100 luminance threshold
    Watermark: labelzoom.Ptr(false),        // an explicit false IS sent
    Label:     &labelzoom.LabelSize{Width: labelzoom.Ptr(4.0), Height: labelzoom.Ptr(6.0)},
    PDF:       &labelzoom.PDFOptions{PageNumber: labelzoom.Ptr(0)},
    ZPL:       &labelzoom.ZPLOptions{CommandsToIgnore: []string{"^PQ"}},
    Data:      []labelzoom.DataRecord{{"sku": "1234"}},
}
```

Two units are routinely misread and are pinned by the shared fixtures:

- `LabelSize.Width` / `.Height` are **inches**, not dots. Omit the whole `Label` to have the
  server detect the size.
- `PDFOptions.PageNumber` is **0-based**. Omit it to convert every page.

`Data` is one record per output label; a single record is wrapped rather than rejected.
`Options.Extra` carries anything the SDK does not model yet — unknown keys are ignored
server-side, so it is a safe forward-compatibility hatch.

## Errors

Every non-2xx response becomes a typed error carrying the status, the message, the raw body, and
the `X-LZ-Request-Id` support handle:

```go
result, err := client.Convert(ctx, request)

var forbidden *labelzoom.ForbiddenError
if errors.As(err, &forbidden) && forbidden.IsPaidFeature {
    // The anonymous tier hit a paywall rather than a permissions problem.
}

var apiErr *labelzoom.APIError
if errors.As(err, &apiErr) {
    log.Printf("request %s failed with %d: %s", apiErr.RequestID, apiErr.Status, apiErr.Message)
}
```

`*BadRequestError`, `*UnauthorizedError`, `*ForbiddenError`, `*NotFoundError`,
`*PayloadTooLargeError`, `*RateLimitedError` and `*ServerError` all unwrap to `*APIError`, so one
`errors.As` catches the lot.

`*ValidationError` deliberately does **not**: it reports a request rejected locally, before any
network call, which is a bug in the calling code rather than a server response. Catching
`*APIError` to implement a fallback will not swallow it.

## Retries

429s, 5xx responses and transport failures are retried automatically — twice by default, for
three attempts — with a 1s/2s/4s backoff under full jitter. A `Retry-After` header is honoured
on any retryable status when it asks for longer than the backoff would wait. No other 4xx is
ever retried.

```go
client, err := labelzoom.New(labelzoom.WithMaxRetries(0))   // disable retrying
```

## Testing your own code

Both seams the retry loop needs are exported, so a test never opens a socket and never sleeps:

```go
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
```

`WithEnvLookup` is worth using even when a test does not care about credentials: without it, a
developer's real `LABELZOOM_API_KEY` changes what the SDK sends.

## Development

```sh
gofmt -l .          # must print nothing
go vet ./...
go test ./...
```

The test suite is the shared conformance fixtures in [`../conformance/`](../conformance/) — the
same 83 cases the .NET, Node, Java, Python and PHP suites run — plus an assertion that it
executed every one of them. `conformance/skips/go.json` is empty: Go compiles, so the two
`typecheck/*` cases are run for real, by building a snippet from `testdata/typecheck/` and
asserting the compiler rejects it.

## License

MIT — see [LICENSE](LICENSE).
