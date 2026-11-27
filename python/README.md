![LabelZoom Logo](https://raw.githubusercontent.com/labelzoom/labelzoom-sdk/main/docs/LabelZoom_Logo_f_400px.png)

# LabelZoom Python SDK

The official Python client for the [LabelZoom API](https://api.labelzoom.com). It converts barcode
labels between ZPL, EPL, IPL, TSPL, DPL, SBPL, CPCL, PDF, LabelZoom XML and JSON, and raster images,
reads ESC/Label, Fingerprint, EZPL, TPCL, NiceLabel, BarTender, and DYMO files, and prints to
Cloud Print printers.

## What the SDK handles

- Retries `429`, `5xx`, and transport failures with backoff, and honors a longer `Retry-After`.
- Raises a typed error for every non-2xx response, with the server's message, the error `code`,
  and the request ID.
- Returns the response as bytes, so PDF, image, and printer-language output that carries binary
  data arrives intact.
- Sends an `Idempotency-Key` with every print, so a retried print doesn't print twice.
- Works without an API key. Anonymous requests use the watermarked free tier; see
  [Rate limits and plans](https://docs.labelzoom.com/reference/rate-limits-and-plans/).
- Provides a synchronous client, `LabelZoomClient`, and an asyncio client,
  `AsyncLabelZoomClient`, with the same methods.
- Ships type hints (`py.typed`), so mypy and your editor check formats and option values.

## Install

The SDK requires Python 3.10 or later. Install the
[`labelzoom-sdk`](https://pypi.org/project/labelzoom-sdk/) package from PyPI:

```sh
pip install labelzoom-sdk
```

The package is named `labelzoom-sdk`, but you import it as `labelzoom`. Its one dependency is
[httpx](https://www.python-httpx.org/).

## Convert a label

A conversion names the source format, the target format, and the document, and then sets source
and target options as keyword arguments. This program converts the first page of a PDF to ZPL for
a 203 dpi printer:

```python
from pathlib import Path

from labelzoom import LabelZoomClient

with LabelZoomClient() as client:            # reads LABELZOOM_API_KEY
    result = client.convert(
        "pdf", "zpl",
        Path("shipping-label.pdf").read_bytes(),
        pdf_page_number=0,                   # source option: 0-based page
        label_width=4, label_height=6,       # source option: inches
        target_dpi=203,                      # target option: the printer's resolution
    )

Path("label.zpl").write_bytes(result.content)
print(result.status, result.content_type, result.request_id)
```

Source options change how the document is read: its label size, page, resolution, merge data, and
grid. Target options change what is written: output resolution, rotation, watermark, and
format-specific settings such as ZPL image compression. Each option acts only for the formats that
use it; see [Options by stage](#options-by-stage). The SDK sends only the options you set, so a
server default applies to everything you leave out.

`result.content` holds the response bytes. Write it as bytes for PDF and image targets, and for
printer-language targets whose labels might carry graphics. `result.text` decodes the bytes with
the response charset, defaulting to UTF-8.

### Choose a credential

| Call | Credential |
|---|---|
| `LabelZoomClient()` | The `LABELZOOM_API_KEY` environment variable, or anonymous when it's unset |
| `LabelZoomClient("API_KEY")` | The key you pass: an `lz_live_` or `lz_test_` key, or a JWT |
| `LabelZoomClient(None)` or `LabelZoomClient("")` | Anonymous, even when `LABELZOOM_API_KEY` is set |

`client.is_authenticated` reports whether the client has a credential. A key the server doesn't
recognize doesn't fail: the request converts anonymously. Anonymous requests get watermarked
output, one label per request, and no multi-page, JSON-target, or
image-to-image conversion. To convert a label other than the first on the free tier, set
`label_index`.

### Fill variable fields

Pass `data` to fill a template's variable fields. Each record produces one label:

```python
template = "^XA^FO50,50^A0N,30,30^FD{name}^FS^FO50,100^A0N,30,30^FD{sku}^FS^XZ"

with LabelZoomClient() as client:
    result = client.convert(
        "zpl", "pdf", template,
        data=[
            {"name": "ACME Corp", "sku": "12345"},
            {"name": "Globex", "sku": "67890"},
        ],
    )                                        # a 2-page PDF
```

A single mapping is one label. Each record must be a mapping; anything else raises
`LabelZoomValidationError` before a request is sent.

### Build options in code

To assemble options at run time, use `ConversionOptions`, a frozen dataclass with the same field
names as the keyword arguments:

```python
from labelzoom import ConversionOptions

preset = ConversionOptions(source_dpi=203, target_dpi=300, label_width=4, label_height=6)
result = client.convert("zpl", "png", zpl, options=preset, rotation=90)
```

Keyword arguments that you pass alongside `options` override its fields. An option that you don't
set holds the `UNSET` sentinel rather than `None`, so `watermark=False` and `rotation=0` are values
the SDK sends.

## Print a label

Printing is a conversion with a printer as the target: `print` takes the printer ID where
`convert` takes the target format. The printer's configuration sets its language, and LabelZoom
converts the document to it, so a PDF prints on a ZPL printer. Printing requires an API key with
the `print` scope, and a printer connected through the LabelZoom Print Agent.

The following program prints a PDF, prints a template, follows the first job, and reads the
quota:

```python
import time
from pathlib import Path

from labelzoom import LabelZoomClient

with LabelZoomClient("API_KEY") as client:
    job = client.print(
        "pdf", "PRINTER_ID", Path("label.pdf").read_bytes(),
        pdf_page_number=0,                   # source options, as for convert
        rotation=90,                         # the one target option a printer takes
        copies=2,
        idempotency_key="ORDER_NUMBER",
    )                                        # PrintJob(job_id, status, copies, idempotent)

    batch = client.print_template(
        "TEMPLATE_ID", "PRINTER_ID",
        [{"orderNumber": "SO-1001", "sku": "A-100"}],
        validate=True,
    )                                        # PrintBatch(jobs, remaining, message)

    details = client.jobs.get(job.job_id)
    while details.status not in ("completed", "failed"):
        time.sleep(2)
        details = client.jobs.get(job.job_id)
    print(details.status, details.error)

    quota = client.get_print_quota()
    print(quota.jobs_today.remaining, quota.jobs_today.reset_at)
```

| Topic | Behavior |
|---|---|
| Sources | Every source format prints except `"url"` and base64 text. Fetch a URL yourself and print its bytes, and decode base64 text before you print it. |
| Options | `print` takes the source options that `convert` takes, plus `rotation`, which turns the finished label in any printer language. |
| Templates | `print_template` takes merge data and `validate` instead of a document and source options. `validate=True` checks every record for missing merge fields before anything prints. |
| Copies | `copies` is 1 to 9999. For `print`, it sends the whole job that many times, so `^PQ3` with `copies=5` prints 15 labels. For `print_template`, it prints each label that many times. |
| Idempotency | Every print sends an `Idempotency-Key`. Set `idempotency_key` to make a repeated call return the original job, with `idempotent=True`, instead of printing again. Without one, the SDK generates a key per call and reuses it on that call's retries. |
| Unfinished batch | When a template batch runs out of time, `batch.remaining` is the number of records not yet submitted. To submit them, send the same call with the same key. |
| Job status | `PrintJob.status` and `PrintJobDetails.status` are strings, such as `queued`, `dispatched`, `printing`, `completed`, and `failed`. New statuses can appear. |
| Daily limit | The daily job limit raises `PrintQuotaExceededError`, which the SDK never retries. The limit resets at 00:00 UTC; check `get_print_quota().jobs_today.remaining` before a large run. |

`PrintJobDetails` also carries `printer_id`, `agent_id`, `source_format`, `copies`, `error`,
`retry_count`, and the `created_date`, `dispatched_date`, `completed_date`, and `retried_date`
timestamps. `PrintQuota` carries `plan`, `agents`, `printers`, `jobs_today`, and `limits`
(`max_payload_bytes`, `max_stored_payload_bytes`, `max_batch_size`). For the endpoints and limits,
see the [Cloud Print API](https://docs.labelzoom.com/reference/cloud-print-api/).

## Formats

```python
SourceFormat = Literal["zpl", "epl", "ipl", "tspl", "dpl", "sbpl",
                       "esclabel", "fingerprint", "ezpl", "cpcl", "tpcl",
                       "nlbl", "lbl", "btw", "dymo",
                       "xml", "json", "pdf", "png", "bmp", "gif",
                       "jpeg", "jpg", "url"]

TargetFormat = Literal["zpl", "epl", "ipl", "tspl", "dpl", "sbpl", "cpcl",
                       "xml", "json", "pdf", "png", "bmp", "gif",
                       "jpeg"]
```

`SOURCE_FORMATS`, `TARGET_FORMATS`, and `PRINT_SOURCES` hold the same values as tuples.

- ESC/Label, Fingerprint, EZPL, TPCL, NiceLabel (`nlbl`, `lbl`), BarTender (`btw`), and DYMO are
  source-only. Send NiceLabel and BarTender files as bytes, never as a decoded string.
- `jpg` and `url` are source-only. `jpg` is sent as `jpeg`. `url` sends a URL as the body, and the
  server fetches it and converts what it finds. Validate the URL first if it comes from untrusted
  input.
- EPL, IPL, TSPL, DPL, SBPL, and CPCL are targets as well as sources, so
  `client.convert("pdf", "epl", body)` is a conversion. Their output is `text/plain` with every
  label concatenated. EPL (`GW`), TSPL (`BITMAP`), IPL (bitmap columns framed by `STX` and `ETX`),
  and SBPL (inline graphics) can carry raw binary, so read `result.content` rather than
  `result.text`. CPCL writes graphics as hex, so its output is plain text.

A target that isn't a `TargetFormat` is a mypy error, and the SDK also raises
`LabelZoomValidationError` before it sends anything:

```python
client.convert("pdf", "url", body)
#                     ^^^^^ error: Argument 2 has incompatible type "Literal['url']";
#                            expected "Literal['zpl', 'epl', 'ipl', 'tspl', ...]"
```

## Options by stage

Options are keyword arguments. The API's nested names are flattened with underscores:
`label.width` is `label_width`, and `pdf.pageNumber` is `pdf_page_number`. Each table gives the
keyword argument and its `params` key.

### Source options

These change how the source document is read. `print` takes all of them.

| Keyword argument | `params` key | Sources | What it does |
|---|---|---|---|
| `source_dpi` | `sourceDpi` | Printer languages and images | The resolution the job or image was made for. Default `203`. PDF, NiceLabel, BarTender, DYMO, and LabelZoom XML and JSON sources return `400`. |
| `label_width`, `label_height` | `label.width`, `label.height` | All | The label size, in inches, not dots. Replaces the size the code declares; omit both to use the declared or detected size. |
| `label_index` | `labelIndex` | All | Converts only this label, 0-based, on every plan. Out of range returns `404`. |
| `data` | `data` | All | Merge data. Each record produces one label. |
| `pdf_page_number` | `pdf.pageNumber` | PDF | Converts one page, 0-based. Omit it to convert every page. |
| `pdf_conversion_mode` | `pdf.conversionMode` | PDF | `"IMAGE"` (default) rasterizes each page, and `"NATIVE"` extracts text and vector content. |
| `zpl_commands_to_ignore` | `zpl.commandsToIgnore` | ZPL and ESC/Label | Commands to skip, such as `["^PQ"]`. |
| `split_rows`, `split_columns` | `split.rows`, `split.columns` | Image or PDF to a printer language, `IMAGE` mode | Cuts each page into a grid of labels, 1 to 20 each. |
| `split_width`, `split_height` | `split.width`, `split.height` | Image or PDF to a printer language, `IMAGE` mode | One label's size on the page, in inches. Omit both to divide the page equally. |
| `split_left`, `split_top`, `split_column_gap`, `split_row_gap` | `split.left`, `split.top`, `split.columnGap`, `split.rowGap` | Image or PDF to a printer language, `IMAGE` mode | Margins and gaps on the page, in inches. |
| `split_skip_blank` | `split.skipBlank` | Image or PDF to a printer language, `IMAGE` mode | Leaves out labels with nothing printable. |
| `scaling` | `scaling` | Image and PDF | The size, in percent, the picture is rendered at. Default `100`. |
| `position_x`, `position_y` | `position.x`, `position.y` | Image or PDF to a printer language | The picture's offset on the label, in dots. A negative value crops it. |
| `color_mode` | `colorMode` | Image and PDF | `"BW"`, `"GRAYSCALE"` (default), or `"COLOR"`. Also a target option for image targets. |
| `darkness` | `darkness` | Image and PDF | The black-and-white threshold, 0 to 100. Default `70`. Also a target option for image targets. |

### Target options

These change what is written. A printer target takes only `rotation`.

| Keyword argument | `params` key | Targets | What it does |
|---|---|---|---|
| `target_dpi` | `targetDpi` | Every target but PDF | The output resolution. Default: `source_dpi`, else `203`. A PDF target returns `400`. |
| `rotation` | `rotation` | All, and printers | Turns the finished label clockwise. Must be a multiple of 90; the SDK raises `LabelZoomValidationError` otherwise. |
| `watermark` | `watermark` | All | Adds a watermark. The free tier always adds one. |
| `dialect` | `dialect` | ZPL | An output dialect, such as `"moca"`. Paid plans only. |
| `zpl_image_compression` | `zpl.imageCompression` | ZPL | `"Z64"` (default) or `"COMPRESSED_HEX"`. |
| `zpl_collapse_copies` | `zpl.collapseCopies` | Image or PDF to ZPL, `IMAGE` mode | Writes each run of identical labels as one label with a `^PQ` count. |
| `layout_across`, `layout_gap`, `layout_fill` | `layout.across`, `layout.gap`, `layout.fill` | Image or PDF to a printer language, `IMAGE` mode | Labels side by side in each row, 1 to 10; the gap between them in inches; and `"SEQUENTIAL"` (default) or `"REPEAT"`. |
| `color_mode`, `darkness` | `colorMode`, `darkness` | PNG, BMP, GIF, and JPEG | `color_mode="BW"` returns a black-and-white image at the `darkness` threshold. |

To change a label's density at the same physical size, set both `source_dpi` and `target_dpi`.
With `source_dpi` alone, the output keeps the source's resolution.

### Other arguments

| Argument | What it does |
|---|---|
| `options` | A `ConversionOptions` holding any of the options above. Keyword arguments override it. `convert` only. |
| `as_base64_text` | Sends the body as base64 `text/plain` instead of the format's own media type. For PDF, image, NiceLabel, and BarTender sources. `convert` only. |
| `extra` | A mapping merged into the top level of `params` as is, for an API parameter the SDK doesn't model. A key in it replaces the one the SDK built, including a whole nested group such as `pdf`. `convert` only. |

The SDK checks `rotation`, `darkness`, both resolutions, and `data` locally. The server checks
the rest, including which formats take which option, and returns `400` with the parameter's name.

## Errors

Every non-2xx response raises a subclass of `LabelZoomError`. Each one carries `status`,
`message`, `request_id` (the `X-LZ-Request-Id` header, for support), `raw_body` (the full
response body), and `code` (the error's stable code, or `None`).

| Error | Status |
|---|---|
| `BadRequestError` | `400` |
| `UnauthorizedError` | `401` |
| `ForbiddenError` | `403`. `is_paid_feature` is `True` when the request needs a paid plan. |
| `NotFoundError` | `404` |
| `PayloadTooLargeError` | `413` |
| `UnprocessableError` | `422`, with a code such as `agent_update_required` or `idempotency_key_reused` |
| `RateLimitedError` | `429`. `retry_after_seconds` holds `Retry-After`. |
| `PrintQuotaExceededError` | `429` with code `daily_job_limit_reached`. A subclass of `RateLimitedError`. |
| `ServerError` | `5xx` |
| `LabelZoomError` | Any other non-2xx status |

Branch on `code` rather than on `message`:

```python
from labelzoom import ForbiddenError, LabelZoomError, UnprocessableError

try:
    client.convert("zpl", "json", zpl)
except ForbiddenError as error:
    if error.is_paid_feature:
        print(f"{error.message} (request {error.request_id})")
except UnprocessableError as error:
    if error.code == "agent_update_required":
        print("Update the LabelZoom Print Agent.")
except LabelZoomError as error:
    print(f"{error.status} {error.code}: {error.message}")
```

`LabelZoomValidationError` means the calling code built a request the SDK can't send, so nothing
reaches the network. It subclasses `ValueError`, not `LabelZoomError`, so a handler for server
errors doesn't catch it. `error.parameter` names the parameter at fault. For every status and
`code`, see [Errors](https://docs.labelzoom.com/reference/errors/).

## Retries

The SDK retries `429`, `5xx`, and transport failures, and makes 3 attempts in total. Before each
retry, it waits a random time of up to 1 second, then up to 2 seconds, or the server's
`Retry-After` when that is longer. `PrintQuotaExceededError` raises at once, and other `4xx`
responses raise without a retry.

To change the policy, set these when you create the client:

```python
client = LabelZoomClient(
    max_retries=0,        # retries after the first attempt; 0 disables retrying; default 2
    timeout=30.0,         # seconds per request; None disables it; default 60
)
```

With a higher `max_retries`, the limit on each wait keeps doubling: 4 seconds, 8 seconds, and so
on.

## Configure the client

`LabelZoomClient` and `AsyncLabelZoomClient` take the same keyword arguments:

| Argument | Default | What it does |
|---|---|---|
| `base_url` | `https://api.labelzoom.com` | The API host. A path prefix is kept, so a reverse proxy at `https://proxy.example.com/labelzoom` works. |
| `timeout` | `60.0` | Seconds per request, or an `httpx.Timeout`. `None` disables it. Large multi-page PDFs are the slow case. |
| `max_retries` | `2` | Retries after the first attempt. `0` disables retrying. |
| `user_agent_suffix` | None | Text appended to the SDK's `User-Agent`. |
| `http_client` | None | Your own `httpx.Client` (or `httpx.AsyncClient`), to share a connection pool. The SDK doesn't close a client it didn't create. |
| `transport` | None | An httpx transport for the client the SDK creates, such as `httpx.MockTransport` in tests. |
| `use_jitter` | `True` | Full jitter on retry delays. Turn it off for deterministic tests. |
| `sleep` | `time.sleep` (`asyncio.sleep`) | The function that waits between retries. |

Use the client as a context manager, or call `close()` (`await aclose()` for the async client)
when you're done with it.

## Use the async client

`AsyncLabelZoomClient` has the same methods and arguments as `LabelZoomClient`. Await each call,
including `client.jobs.get(job_id)`:

```python
import asyncio

from labelzoom import AsyncLabelZoomClient

ZPL = "^XA^FO50,50^ADN,36,20^FDLabelZoom^FS^XZ"


async def main() -> None:
    async with AsyncLabelZoomClient() as client:
        result = await client.convert("zpl", "png", ZPL, source_dpi=203, target_dpi=300)
        print(result.status, result.content_type, len(result.content))


asyncio.run(main())
```

Pass an `httpx.AsyncClient` as `http_client`, an `httpx.AsyncBaseTransport` as `transport`, and an
async function as `sleep`.

## Test your own code

The transport and the sleep function are injectable, so you can test code that calls the SDK
without a network and without waiting out retry delays:

```python
import httpx

from labelzoom import LabelZoomClient

slept: list[float] = []
client = LabelZoomClient(
    None,
    transport=httpx.MockTransport(lambda request: httpx.Response(200, text="^XA^XZ")),
    use_jitter=False,
    sleep=slept.append,
)
result = client.convert("pdf", "zpl", b"%PDF-1.7")
assert result.text == "^XA^XZ"
```

## Migrate from 1.x

The method names are unchanged. Version 2.0 splits `dpi` in two, adds options, and adds printing:

| 1.x | 2.0 |
|---|---|
| `dpi=N` with a printer-language or image source and a PDF target | `source_dpi=N` |
| `dpi=N` with a PDF, NiceLabel, BarTender, DYMO, or LabelZoom XML or JSON source | `target_dpi=N` |
| `dpi=N` with any other source and target | `source_dpi=N, target_dpi=N` for the 1.x output, or only the one you mean |
| `ConversionOptions(dpi=N)` | `ConversionOptions(source_dpi=N, target_dpi=N)`, following the same rules |
| `extra={"labelIndex": 0}` | `label_index=0` |
| `extra={"split": {...}}` | `split_rows`, `split_columns`, and the other `split_*` arguments |
| `extra={"layout": {...}}` | `layout_across`, `layout_gap`, and `layout_fill` |
| `extra={"zpl": {"collapseCopies": True}}` | `zpl_collapse_copies=True` |
| A `422` raised as `LabelZoomError` | `UnprocessableError`, a subclass of `LabelZoomError` |
| Matching an error's `message` | `error.code` |
| No print support | `print`, `print_template`, `jobs.get`, and `get_print_quota` |

Label size, PDF page, and merge data keep their names (`label_width`, `label_height`,
`pdf_page_number`, and `data`). They are source options, so `print` takes them too.

## Contributing

The SDK lives in the `python/` directory of
[labelzoom-sdk](https://github.com/labelzoom/labelzoom-sdk). To set up a development environment
and run the checks, run these commands from that directory:

```sh
pip install -e ".[dev]"
pytest            # offline: no key, no network
mypy
ruff check
```

The test suite runs the shared conformance fixtures through both the sync and async clients and
checks that it ran every fixture. See
[docs/CONFORMANCE.md](https://github.com/labelzoom/labelzoom-sdk/blob/main/docs/CONFORMANCE.md).
Each language SDK has its own version, and the contract carries its own version in
`conformance/spec.json`.

## License

MIT. See [LICENSE](https://github.com/labelzoom/labelzoom-sdk/blob/main/LICENSE).
