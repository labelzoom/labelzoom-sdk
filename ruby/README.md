![LabelZoom Logo](../docs/LabelZoom_Logo_f_400px.png)

# LabelZoom Ruby SDK

The `labelzoom` gem is the official Ruby client for the [LabelZoom API](https://api.labelzoom.com).
Use it to convert barcode labels between printer languages, PDF, images, and label design files,
and to print them on any printer connected through the LabelZoom Print Agent.

## What the SDK handles

- Retries `429` responses, `5xx` responses, and network failures, and waits as long as a
  `Retry-After` header asks.
- Raises a typed error for every non-2xx response, with the status, message, error `code`, and
  request ID.
- Returns every result as binary-safe bytes, so PDF, image, and printer-language output arrives
  intact.
- Sends an `Idempotency-Key` with every print, so a retry never prints a label twice.
- Works without an API key, on the watermarked free tier. See
  [Plans and limits](https://docs.labelzoom.com/reference/rate-limits-and-plans/).
- Needs no other gems: it uses `net/http`, `json`, `uri`, and `openssl` from the standard library.

## Install

The gem requires Ruby 3.1 or later.

```sh
gem install labelzoom
```

If you use Bundler, add the gem to your `Gemfile` instead:

```ruby
gem "labelzoom", "~> 2.0"
```

## Convert a label

A conversion is a pipeline: choose the source format, set how the source is read, choose the target
format, set what is written, and then run it. In Ruby, one `convert` call takes the source format,
the target format, and the document, and each option is a keyword argument.

```ruby
require "labelzoom"

client = LabelZoom::Client.new        # reads LABELZOOM_API_KEY; works without one

result = client.convert(
  :pdf, :zpl, File.binread("shipping-label.pdf"),
  pdf: { page_number: 0 },            # source: read the first page (0-based)
  label: { width: 4, height: 6 },     # source: the label size, in inches
  target_dpi: 203                     # target: write ZPL for a 203 dpi printer
)

result.save("label.zpl")
```

Source options set how the document is read, such as its label size, its page, its resolution, and
the records that fill its fields. Target options set what is written, such as the output
resolution, the rotation, and ZPL image compression. Both kinds are keyword arguments on the same
call; [Options by stage](#options-by-stage) lists which is which and the formats each one acts on.

The SDK sends only the options you pass and fills in no defaults of its own, so the server's
defaults apply to the rest. `watermark: false` is sent, because you set it.

### Set the API key

The client reads your API key from the `LABELZOOM_API_KEY` environment variable. To pass a key, use
`LabelZoom::Client.new(api_key: "lz_live_...")`. To force the free tier, pass `api_key: nil` or an
empty string; the client then ignores the environment variable. `client.authenticated?` returns
whether the client has a key.

Without a key, the API serves the free tier: watermarked output, the first label only, a 1 MB
request limit, and no multi-page, JSON-target, or image-to-image conversion.

### Read the result

`convert` returns a `LabelZoom::ConversionResult`:

| Method | Returns |
| --- | --- |
| `bytes` | The response body as a binary (`ASCII-8BIT`) String. This is a String of bytes, not an Array of Integers. |
| `text` | The body decoded with the response's charset, or UTF-8 when it names none. |
| `save(path)` | Writes `bytes` to `path` in binary mode, and returns `path`. |
| `content_type` | The response `Content-Type`. |
| `status` | The HTTP status, always 2xx. |
| `request_id` | The `X-LZ-Request-Id` response header. Give it to LabelZoom support. |

Write PDF and image output with `save` or `File.binwrite`, never with `File.write`. Printer-language
output is `text/plain`, but EPL's `GW`, TSPL's `BITMAP`, IPL graphics, and SBPL graphics embed raw
binary, so read `bytes` rather than `text` whenever a label might carry graphics. CPCL writes
graphics as hex, so its output is plain text.

## Print a label

A printer is a target. `print` takes the same arguments as `convert`, with the printer ID where the
target format goes. The printer's own configuration sets its language, and LabelZoom converts the
document to it, so a PDF prints on a ZPL printer. Printing needs an API key with the `print` scope.

### Print a document

```ruby
client = LabelZoom::Client.new(api_key: "lz_live_...")

job = client.print(
  :pdf, printer_id, File.binread("label.pdf"),
  pdf: { page_number: 0 },            # any source option
  rotation: 90,                       # the one target option a printer takes
  copies: 2,
  idempotency_key: order_number
)

job.job_id                            # also job.status, job.copies, and job.idempotent?
```

`print` returns a `LabelZoom::PrintJob`. Its `status` is `"dispatched"` when an online agent took
the job, and `"queued"` when no agent is online.

- Every source format prints, including NiceLabel, BarTender, and DYMO files, except a URL and
  base64 text. Fetch or decode the document and print its bytes. `:url` and `base64_text: true`
  raise `LabelZoom::ValidationError`.
- `print` takes every source option, and `rotation:` is the only target option it takes. The
  other target options, such as `target_dpi:`, depend on the output language, which the printer
  sets, so they raise `LabelZoom::ValidationError`. Of the `zpl:` keys, `print` takes only
  `commands_to_ignore:`.
- `copies:` sends the whole job that many times, from 1 to 9999, and multiplies any quantity in the
  document: `^PQ3` with 5 copies prints 15 labels.
- Every print carries an `Idempotency-Key`. To make a repeated call return the original job
  instead of printing again, pass your own `idempotency_key:`, such as an order number. Without
  one, the SDK generates a key for each call and reuses it on that call's retries, so a retry
  can't print twice. `job.idempotent?` is true when the server returned the original job.

### Print a template

A template published from LabelZoom Studio or the web designer is a source of its own. It takes
merge data instead of source options, one record per label.

```ruby
batch = client.print_template(
  template_id, printer_id,
  data: [{ orderNumber: "SO-1001", sku: "A-100" }],
  validate: true,                     # check every record for missing fields before printing
  copies: 1,
  idempotency_key: "SO-1001"
)

batch.jobs                            # one LabelZoom::PrintJob per record
batch.remaining                       # records not yet submitted
```

`print_template` returns a `LabelZoom::PrintBatch` for any number of records. A single Hash in
`data:` is one label; omit `data:` for a template with no merge fields. If `remaining` isn't 0, the
batch ran out of time, and `message` says why: send the same request with the same key to submit
the rest. The records already submitted don't print again.

### Follow a job

```ruby
details = client.jobs.get(job.job_id)

details.status                        # queued, dispatched, printing, completed, or failed
details.error                         # why the job failed, or nil
```

`jobs.get` returns a `LabelZoom::PrintJobDetails` with `id`, `printer_id`, `agent_id`,
`source_format`, `copies`, `status`, `error`, `retry_count`, and the `created_date`,
`dispatched_date`, `completed_date`, and `retried_date` timestamps as `Time` objects. Each value is
`nil` when it isn't set. To follow a job to the end, call `jobs.get` until `status` is `completed`
or `failed`. Treat `status` as an open set of strings.

### Check the quota

```ruby
quota = client.print_quota

quota.jobs_today.remaining            # prints left today
quota.jobs_today.reset_at             # 00:00 UTC, as a Time
```

`print_quota` returns a `LabelZoom::PrintQuota` with `plan`; `agents`, `printers`, and `jobs_today`,
each with `used`, `limit`, and `remaining`; and `limits`, with `max_payload_bytes`,
`max_stored_payload_bytes`, and `max_batch_size`. `agents` also has `pending`.

When the daily job limit is used up, a print raises `LabelZoom::PrintQuotaExceededError`. The SDK
never retries it, because the limit resets at 00:00 UTC. Check `print_quota` to stop before you
reach it.

## Formats

| Role | Formats |
| --- | --- |
| Sources | `:zpl` `:epl` `:ipl` `:tspl` `:dpl` `:sbpl` `:esclabel` `:fingerprint` `:ezpl` `:cpcl` `:tpcl` `:nlbl` `:lbl` `:btw` `:dymo` `:xml` `:json` `:pdf` `:png` `:bmp` `:gif` `:jpeg` `:jpg` `:url` |
| Targets | `:zpl` `:epl` `:ipl` `:tspl` `:dpl` `:sbpl` `:cpcl` `:xml` `:json` `:pdf` `:png` `:bmp` `:gif` `:jpeg` |
| Print sources | Every source except `:url` |

The printer languages ESC/Label, Fingerprint, EZPL, and TPCL, and the design-file formats NiceLabel
(`:nlbl`, `:lbl`), BarTender (`:btw`), and DYMO (`:dymo`), are source-only. Send NiceLabel and
BarTender files as bytes, read with `File.binread`.

`:jpg` is an input spelling that becomes `jpeg` on the wire, and `:url` makes the server fetch the
document whose URL is the body, so neither is a target. A format that isn't a valid source or
target raises `LabelZoom::ValidationError` before any request is sent.

The printer languages round-trip: `pdf` to `epl` and `zpl` to `tspl` are real conversions.

## Options by stage

Each option is a keyword argument to `convert`. Grouped options are nested hashes, such as
`label: { width: 4, height: 6 }`. A misspelled key inside a nested hash raises
`LabelZoom::ValidationError`. `convert` doesn't check top-level keyword names, so a misspelled
one, such as `taget_dpi:`, has no effect; `print` raises for one.

### Source options

| Keyword | Source formats | What it does |
| --- | --- | --- |
| `label: { width:, height: }` | All | The label size, in inches, not dots. It replaces the size the label code declares. Omit it to use the declared size, or the size of the content. |
| `label_index:` | All | Converts only this label, 0-based, on every plan. For image or PDF to ZPL, it counts the labels after `split:`, page by page. |
| `data:` | All | Merge-field values, one Hash per output label. A single Hash is one label. |
| `source_dpi:` | Printer languages and images | The resolution the source was made for. A PDF, design-file, XML, or JSON source returns `400`. |
| `pdf: { page_number: }` | PDF | Converts one page, 0-based. Omit it to convert every page. |
| `pdf: { conversion_mode: }` | PDF | `"IMAGE"` (the default) rasterizes each page; `"NATIVE"` extracts the text and vector content. |
| `zpl: { commands_to_ignore: }` | ZPL and ESC/Label | ZPL commands to skip, such as `["^PQ"]`. |
| `split: { rows:, columns:, width:, height:, left:, top:, column_gap:, row_gap:, skip_blank: }` | Image or PDF to ZPL | Cuts each page into a grid of labels. Lengths are in inches. Omit `width:` and `height:` to divide the page equally. |
| `scaling:` | Image and PDF | The size the picture is rendered at, in percent. The server default is 100. |
| `position: { x:, y: }` | Image and PDF | The picture's offset on the label, in output dots from the top-left corner. A negative value crops it. |
| `color_mode:` | Image and PDF | `"BW"`, `"GRAYSCALE"` (the default), or `"COLOR"`. |
| `darkness:` | Image and PDF | The black-and-white threshold, 0 to 100. |

### Target options

| Keyword | Target formats | What it does |
| --- | --- | --- |
| `target_dpi:` | Every target except PDF | The output resolution. It defaults to the source's resolution, else 203. A PDF target returns `400`. |
| `rotation:` | All, and printers | Turns the finished label. It must be a multiple of 90. |
| `watermark:` | All | `true` adds a watermark. |
| `dialect:` | ZPL | The output dialect, such as `"moca"`. Paid plans only. |
| `zpl: { image_compression: }` | ZPL | `"Z64"` (the default) or `"COMPRESSED_HEX"`. |
| `zpl: { collapse_copies: true }` | Image or PDF to ZPL | Writes each run of identical labels as one label with a `^PQ` count. |
| `layout: { across:, gap:, fill: }` | Image or PDF to ZPL | Places labels side by side in each printed row. `gap:` is in inches. `fill:` is `"SEQUENTIAL"` (the default) or `"REPEAT"`. |
| `color_mode:`, `darkness:` | PNG, BMP, GIF, and JPEG | `color_mode: "BW"` returns a black-and-white image at the `darkness:` threshold. |

`color_mode:` and `darkness:` are one keyword each, so a value you set acts on an image or PDF
source and on an image target alike. `zpl:` holds keys for both stages.

`split:`, `layout:`, and `zpl: { collapse_copies: }` need a PDF in `IMAGE` mode, the default. With
`NATIVE`, a split grid or more than 1 across returns `400`, and `collapse_copies:` has no effect.

### Other keywords

| Keyword | What it does |
| --- | --- |
| `base64_text: true` | Sends the body as base64 text with `Content-Type: text/plain`, for the binary sources: PDF, images, NiceLabel, and BarTender. `print` doesn't take it. |
| `extra:` | A Hash of parameters the SDK doesn't model, merged into the request as is. The server ignores keys it doesn't recognize. |

The SDK raises `LabelZoom::ValidationError` before sending a request when `rotation:` isn't a
multiple of 90, `source_dpi:` or `target_dpi:` isn't a positive whole number, `darkness:` is
outside 0 to 100, or the body is empty.

## Errors

Every non-2xx response raises a subclass of `LabelZoom::APIError`, so one `rescue` catches them
all:

| Error | Raised for |
| --- | --- |
| `BadRequestError` | `400`: a malformed request or an invalid conversion path. |
| `UnauthorizedError` | `401`: the API key was rejected. |
| `ForbiddenError` | `403`: the key isn't entitled to the operation. `paid_feature?` is true when the operation needs a paid plan. |
| `NotFoundError` | `404`, including a `label_index:` past the last label. |
| `PayloadTooLargeError` | `413`: the body is over the limit. |
| `UnprocessableError` | `422`: the request is well formed but can't be carried out. |
| `RateLimitedError` | `429`. `retry_after_seconds` holds the `Retry-After` value. |
| `PrintQuotaExceededError` | `429` with code `daily_job_limit_reached`. A subclass of `RateLimitedError`. |
| `ServerError` | `5xx`, after the retries run out. |
| `APIError` | Any other non-2xx status, such as `406` or `409`. |

Each one carries `status`, `message`, `code`, `request_id`, and `raw_body`. `code` is the error's
stable code, such as `"agent_update_required"`, or `nil` when the response has none, so branch on
`code` rather than on the message. `message` is capped at 512 characters; `raw_body` is the whole
body. Conversion responses carry a request ID; Cloud Print responses don't, so `request_id` is
`nil` for a print.

```ruby
begin
  client.print(:zpl, printer_id, zpl, copies: 3)
rescue LabelZoom::PrintQuotaExceededError
  warn "The daily print limit is used up. It resets at 00:00 UTC."
rescue LabelZoom::UnprocessableError => e
  warn "Update the Print Agent." if e.code == "agent_update_required"
rescue LabelZoom::APIError => e
  warn "Request #{e.request_id} failed with #{e.status} (#{e.code}): #{e.message}"
end
```

Two errors aren't `APIError`s:

- `LabelZoom::TransportError` means a request got no response after every retry. It descends from
  `LabelZoom::Error`, the base of every error the gem raises.
- `LabelZoom::ValidationError` means the SDK rejected the request before sending it, which is a
  bug in the calling code. It descends from `ArgumentError`, so a `rescue LabelZoom::APIError` for
  a fallback doesn't swallow it. `parameter` names the option at fault.

## Retries

The client retries `429` responses, `5xx` responses, and network failures twice by default, for
three attempts in all. It waits 1, 2, and then 4 seconds with full jitter, and waits longer when a
`Retry-After` header asks for more. The daily print limit's `429` is raised at once. No other `4xx`
is retried.

To change the number of retries, set `max_retries:`. To turn retrying off, set it to 0:

```ruby
client = LabelZoom::Client.new(max_retries: 0)
```

A print keeps the same `Idempotency-Key` on every retry.

## Configure the client

`LabelZoom::Client.new` takes these keyword arguments:

| Keyword | Default | What it does |
| --- | --- | --- |
| `api_key:` | `LABELZOOM_API_KEY` | An `lz_live_` or `lz_test_` key, or a JWT. `nil` or an empty string forces the free tier. |
| `base_url:` | `https://api.labelzoom.com` | The API host. A path prefix is kept, so a proxy at `https://proxy.example.com/labelzoom` works. |
| `max_retries:` | `2` | Retries after the first attempt. `0` turns retrying off. |
| `timeout:` | `60` | The open and read timeout for each attempt, in seconds. |
| `user_agent_suffix:` | None | Text added to the end of the SDK's `User-Agent`. |
| `http_builder:` | None | A callable that takes the request `URI` and returns the `Net::HTTP` instance to use, for proxies and custom TLS. |
| `sleeper:` | `Kernel.sleep` | A callable that waits between retries. |
| `jitter:` | `true` | Full jitter on the retry delay. |
| `env:` | `ENV` | Where the client looks up `LABELZOOM_API_KEY`. |

`client.build_uri(:zpl, :png)` returns the URL a conversion is posted to, which helps when you debug
a proxy or a `base_url:`.

## Test your own code

The sleeper and the environment lookup are injectable, so a test never sleeps and never picks up a
developer's real key:

```ruby
slept = []
client = LabelZoom::Client.new(
  sleeper: ->(seconds) { slept << seconds },
  jitter: false,
  env: {}
)
```

To stub HTTP, use [WebMock](https://github.com/bblimke/webmock). It intercepts at the `Net::HTTP`
layer, so your test runs the SDK's real request code.

## Migrate from 1.x

The `convert` call, its nested-hash options, and `ConversionResult` are unchanged. These calls
change:

| 1.x | 2.0 |
| --- | --- |
| `gem "labelzoom", "~> 1.0"` | `gem "labelzoom", "~> 2.0"` |
| `dpi: 300` | `source_dpi: 300` for the resolution the source was made for, `target_dpi: 300` for the output's, or both. Drop `source_dpi:` for a PDF, design-file, XML, or JSON source, and `target_dpi:` for a PDF target: each returns `400` there. 2.0 doesn't read `dpi:`, and ignores it without an error. |
| `rescue LabelZoom::APIError` for a `422` | `rescue LabelZoom::UnprocessableError`, which is still an `APIError`. |
| Matching `e.message` | `e.code` |
| None | `client.print`, `client.print_template`, `client.jobs.get`, and `client.print_quota`. |
| None | `label_index:`, `split:`, `layout:`, and `zpl: { collapse_copies: }`. |
| None | The `:cpcl` target, and the `:esclabel`, `:fingerprint`, `:ezpl`, `:cpcl`, `:tpcl`, `:nlbl`, `:lbl`, `:btw`, and `:dymo` sources. |

## Contributing

To run the linter and the tests:

```sh
cd ruby
bundle install
bundle exec rake        # rubocop, then rspec
```

`bundle exec rubocop` and `bundle exec rspec` run each one alone. The spec suite runs WebMock with
network connections disabled, so it sends no requests.

The suite runs every shared fixture in [`../conformance/`](../conformance/), the cases every
LabelZoom SDK runs, and asserts that it ran all of them. See
[`../docs/CONFORMANCE.md`](../docs/CONFORMANCE.md) for how the fixtures work, and
[`../docs/API_CONTRACT.md`](../docs/API_CONTRACT.md) for the rules they check.

Ruby skips the two `typecheck/*` cases in
[`../conformance/skips/ruby.json`](../conformance/skips/ruby.json), because Ruby has no compile
step. `spec/labelzoom/formats_spec.rb` asserts the run-time check that the skip relies on: a
source-only format passed as a target raises `LabelZoom::ValidationError`.

## License

MIT. See [LICENSE](LICENSE).
