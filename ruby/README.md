![LabelZoom Logo](../docs/LabelZoom_Logo_f_400px.png)

# LabelZoom Ruby SDK

Official Ruby client for the [LabelZoom API](https://api.labelzoom.com). Converts barcode labels
between ZPL, EPL, IPL, TSPL, DPL, SBPL, CPCL, PDF, LabelZoom XML/JSON, and raster images, reads
ESC/Label, Fingerprint, EZPL, TPCL, NiceLabel, BarTender and DYMO files, and prints labels through
LabelZoom Cloud Print.

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
                        target_dpi: 300,
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

**Sources:** `:zpl` `:epl` `:ipl` `:tspl` `:dpl` `:sbpl` `:esclabel` `:fingerprint` `:ezpl`
`:cpcl` `:tpcl` `:nlbl` `:lbl` `:btw` `:dymo` `:xml` `:json` `:pdf` `:png` `:bmp` `:gif` `:jpeg`
`:jpg` `:url`

**Targets:** `:zpl` `:epl` `:ipl` `:tspl` `:dpl` `:sbpl` `:cpcl` `:xml` `:json` `:pdf` `:png`
`:bmp` `:gif` `:jpeg`

The newer printer languages (ESC/Label, Fingerprint, EZPL, TPCL) and the design-file formats
(NiceLabel, BarTender, DYMO) are source-only. Send NiceLabel and BarTender files as bytes. CPCL is
a target too, and writes graphics as hex, so its output is plain text.

`:jpg` is an input spelling that normalizes to `jpeg` on the wire, and `:url` tells the server to
go fetch a document rather than naming a format — so neither is a target. The statically typed
SDKs make that a compile error; Ruby raises `LabelZoom::ValidationError` (an `ArgumentError`) on
the call, before any request goes out.

The printer languages round-trip: `pdf`→`epl` and `zpl`→`tspl` are real conversions. Their output
is `text/plain`, but every printer language can inline raw binary (EPL's `GW` and TSPL's `BITMAP`,
for example), so read `result.bytes` rather than `result.text` whenever a label might carry
graphics.

## Options

Options are keyword arguments with **nested hashes**, and **only what you pass is sent** — the SDK
never fills in a client-side default, so a change to a server default reaches you without a gem
upgrade.

```ruby
client.convert(:zpl, :png, zpl,
               target_dpi: 300,                           # server default: the source's, else 203
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

For image or PDF to ZPL, these shape how many labels come back:

| Keyword | Notes |
|---|---|
| `zpl: { collapse_copies: true }` | each run of identical labels becomes one label with a `^PQ` count |
| `layout: { across:, gap:, fill: }` | labels side by side per row; `gap` in **inches**; `fill` is `"SEQUENTIAL"` (default) or `"REPEAT"` |
| `split: { rows:, columns:, width:, height:, left:, top:, column_gap:, row_gap:, skip_blank: }` | cuts each page into a grid of labels; lengths in **inches**; omit `width:`/`height:` to divide the page equally |
| `label_index:` | **0-based**; converts only this label, on every plan |

`collapse_copies:`, `layout:` and `split:` need a PDF in `IMAGE` mode, the default. With `NATIVE`, more than 1 across or a split grid returns `400`, and `collapse_copies:` has no effect.

`data:` is one record per output label; a single Hash is wrapped rather than rejected. `extra:`
carries anything the SDK does not model yet — unknown keys are ignored server-side, so it is a
safe forward-compatibility hatch.

A misspelled key raises rather than vanishing. Ruby has no compiler to catch
`label: { widht: 4 }`, and the server ignores keys it does not recognize, so a silent drop would
hand you a wrong label and no signal.

## Print

Send a document to a printer connected through the LabelZoom Print Agent. The printer's own
configuration sets its language, and LabelZoom converts the document to it, so a PDF prints on a
ZPL printer. There is no target format. Printing needs an API key with the `print` scope.

```ruby
client = LabelZoom::Client.new(api_key: "lz_live_...")

job = client.print(printer_id, :pdf, File.binread("label.pdf"),
                   pdf: { page_number: 0 },       # the same source options as convert
                   copies: 2,
                   idempotency_key: order_number)
job.job_id                                         # also job.status, job.copies, job.idempotent?

batch = client.print_template(printer_id, template_id,
                              data: [{ orderNumber: "SO-1001", sku: "A-100" }])
batch.jobs                                         # one LabelZoom::PrintJob per record

details = client.jobs.get(job.job_id)
details.status                                     # queued, dispatched, printing, completed, failed

client.print_quota.jobs_today.remaining
```

- `print` takes `:zpl` `:epl` `:ipl` `:tspl` `:dpl` `:sbpl` `:fingerprint` `:cpcl` `:xml`
  `:json` `:pdf` `:png` `:bmp` `:gif` `:jpeg` and `:jpg`. To print another format, convert it to
  the printer's language first. It accepts only source options: a target option such as
  `target_dpi:` raises `LabelZoom::ValidationError`.
- `copies:` sends the whole job that many times, from 1 to 9999, and multiplies any quantity in the
  document: `^PQ3` with 5 copies prints 15 labels.
- Every print carries an `Idempotency-Key`. Pass your own `idempotency_key:` to make a repeated
  call return the original job instead of printing again; without one, the SDK generates a key per
  call, so its own retries can't print twice.
- A template batch that runs out of time returns a nonzero `remaining`, the number of records not
  yet submitted. Send the same request with the same key to submit them.
- The daily job limit raises `LabelZoom::PrintQuotaExceededError`, which is never retried, because
  the limit resets at 00:00 UTC.

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
`UnprocessableError` (422), `RateLimitedError` and `ServerError` all descend from
`LabelZoom::APIError`, so one `rescue` catches the lot. `PrintQuotaExceededError` descends from
`RateLimitedError`, so a `rescue LabelZoom::RateLimitedError` catches it too.

Each error carries `code`, the error's stable code such as `"agent_update_required"`, or `nil`
when it has none. Branch on `code` rather than on the message.

`LabelZoom::ValidationError` deliberately does **not**: it reports a request rejected locally,
before any network call, which is a bug in the calling code rather than a server response. It
descends from `ArgumentError`, so rescuing `APIError` to implement a fallback will not swallow it.

## Retries

429s, 5xx responses and transport failures are retried automatically — twice by default, for three
attempts — with a 1s/2s/4s backoff under full jitter. A `Retry-After` header is honored on any
retryable status when it asks for longer than the backoff would wait. The daily print limit is the
exception: its 429 is raised at once. No other 4xx is ever retried.

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
