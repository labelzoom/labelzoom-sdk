![LabelZoom Logo](../docs/LabelZoom_Logo_f_400px.png)

# LabelZoom Ruby SDK

Official Ruby client for the [LabelZoom API](https://api.labelzoom.com). Converts barcode labels
between ZPL, EPL, TSPL, DPL, PDF, LabelZoom XML/JSON, and raster images.

Ruby 3.1+. **No runtime dependencies** — `net/http`, `json`, `uri` and `openssl` are all standard
library.

## Install

```sh
gem install labelzoom
```

```ruby
gem "labelzoom", "~> 1.0"
```

<details>
<summary>Build from source</summary>

```sh
git clone https://github.com/labelzoom/labelzoom-sdk.git
cd labelzoom-sdk/ruby
bundle install && bundle exec rake
```
</details>

## Quick start

**An API key is optional.** Without one you get the free tier — watermarked output, first label
only, a 1 MB request cap, and no multi-page, JSON-target, or image-to-image conversion.

```ruby
require "labelzoom"

client = LabelZoom::Client.new          # anonymous; this works

result = client.convert(:zpl, :png, "^XA^FO20,20^A0N,28^FDHello^FS^XZ",
                        dpi: 300,
                        label: { width: 4, height: 6 })

result.save("label.png")
```

With a credential:

```ruby
client = LabelZoom::Client.new(api_key: "lz_live_...")
```

Passing nothing reads `LABELZOOM_API_KEY` from the environment. Passing `api_key: nil` — or an
empty string — forces the free tier and suppresses that fallback.

## Formats

**Sources (13):** `:zpl` `:epl` `:tspl` `:dpl` `:xml` `:json` `:pdf` `:png` `:bmp` `:gif` `:jpeg`
`:jpg` `:url`

**Targets (11):** `:zpl` `:epl` `:tspl` `:dpl` `:xml` `:json` `:pdf` `:png` `:bmp` `:gif` `:jpeg`

`:jpg` is an input spelling that normalizes to `jpeg` on the wire, and `:url` tells the server to
go fetch a document rather than naming a format — so neither is a target. The statically typed
SDKs make that a compile error; Ruby raises `LabelZoom::ValidationError` (an `ArgumentError`) on
the call, before any request goes out.

The printer languages round-trip: `pdf`→`epl` and `zpl`→`tspl` are real conversions. Their output
is `text/plain`, but EPL's `GW` and TSPL's `BITMAP` commands inline raw binary, so read
`result.bytes` rather than `result.text` whenever a label might carry graphics.

## Options

Options are keyword arguments with **nested hashes**, and **only what you pass is sent** — the SDK
never fills in a client-side default, so a change to a server default reaches you without a gem
upgrade.

```ruby
client.convert(:zpl, :png, zpl,
               dpi: 300,                                  # server default 203
               rotation: 90,                              # must be a multiple of 90
               scaling: 75.0,                             # percent; server default 100
               color_mode: "GRAYSCALE",
               darkness: 60,                              # 0-100 luminance threshold
               position: { x: 5, y: 15 },
               watermark: false,                          # an explicit false IS sent
               label: { width: 4.0, height: 6.0 },
               pdf: { conversion_mode: "IMAGE", page_number: 0 },
               zpl: { commands_to_ignore: ["^PQ"], image_compression: "Z64" },
               data: [{ sku: "1234" }])
```

Two units are routinely misread and are pinned by the shared fixtures:

- `label:` is in **inches**, not dots. Omit it entirely to have the server detect the size.
- `pdf: { page_number: }` is **0-based**. Omit it to convert every page.

`data:` is one record per output label; a single Hash is wrapped rather than rejected. `extra:`
carries anything the SDK does not model yet — unknown keys are ignored server-side, so it is a
safe forward-compatibility hatch.

A misspelled key raises rather than vanishing. Ruby has no compiler to catch
`label: { widht: 4 }`, and the server ignores keys it does not recognize, so a silent drop would
hand you a wrong label and no signal.

## Errors

Every non-2xx response becomes a typed error carrying the status, the message, the raw body, and
the `X-LZ-Request-Id` support handle:

```ruby
begin
  client.convert(:zpl, :json, zpl)
rescue LabelZoom::ForbiddenError => e
  warn "paywall" if e.paid_feature?
rescue LabelZoom::APIError => e
  warn "request #{e.request_id} failed with #{e.status}: #{e.message}"
end
```

`BadRequestError`, `UnauthorizedError`, `ForbiddenError`, `NotFoundError`, `PayloadTooLargeError`,
`RateLimitedError` and `ServerError` all descend from `LabelZoom::APIError`, so one `rescue`
catches the lot.

`LabelZoom::ValidationError` deliberately does **not**: it reports a request rejected locally,
before any network call, which is a bug in the calling code rather than a server response. It
descends from `ArgumentError`, so rescuing `APIError` to implement a fallback will not swallow it.

## Retries

429s, 5xx responses and transport failures are retried automatically — twice by default, for three
attempts — with a 1s/2s/4s backoff under full jitter. A `Retry-After` header is honoured on any
retryable status when it asks for longer than the backoff would wait. No other 4xx is ever retried.

```ruby
client = LabelZoom::Client.new(max_retries: 0)   # disable retrying
```

## Testing your own code

The sleeper and the environment lookup are both injectable, so a test never sleeps and never picks
up a developer's real key:

```ruby
slept = []
client = LabelZoom::Client.new(
  sleeper: ->(seconds) { slept << seconds },
  jitter: false,
  env: {}
)
```

Stub HTTP with [WebMock](https://github.com/bblimke/webmock), which is what this gem's own suite
uses — it intercepts at the `Net::HTTP` layer, so the real request-construction path is exercised
rather than bypassed.

## Development

```sh
bundle install
bundle exec rake        # rubocop + rspec
```

The spec suite is the shared conformance fixtures in [`../conformance/`](../conformance/) — the
same cases the .NET, Node, Java, Python, PHP and Go suites run — plus an assertion that it
executed every one of them.

Ruby declares the two `typecheck/*` cases skipped in
[`../conformance/skips/ruby.json`](../conformance/skips/ruby.json), because Ruby has no compile
step. `spec/labelzoom/formats_spec.rb` is what makes that skip's stated reason true rather than
merely convenient: it asserts the runtime guard the skip claims exists.

## License

MIT — see [LICENSE](LICENSE).
