![LabelZoom Logo](https://raw.githubusercontent.com/labelzoom/labelzoom-sdk/main/docs/LabelZoom_Logo_f_400px.png)

# LabelZoom Python SDK

Official Python client for the [LabelZoom API](https://api.labelzoom.com). Converts barcode
labels between ZPL, EPL, IPL, TSPL, DPL, SBPL, CPCL, PDF, LabelZoom XML/JSON, and raster images, and reads
ESC/Label, Fingerprint, EZPL, TPCL, NiceLabel, BarTender and DYMO files. It also prints to Cloud
Print printers.

Python 3.10+. Fully typed (`py.typed`), sync and async clients, one dependency
([httpx](https://www.python-httpx.org/)).

## Install

```sh
pip install labelzoom-sdk
```

The distribution is `labelzoom-sdk`; the import is `labelzoom`.

> **Stable.** The public API is covered by a shared conformance suite that every language SDK
> runs against the same fixtures. Each SDK is versioned independently, and the contract carries
> its own version in `conformance/spec.json`.

## Quick start

**An API key is optional.** Without one you get the free tier — watermarked output, first label
only, a 1 MB request cap, and no multi-page, JSON-target, or image-to-image conversion.

```python
from pathlib import Path
from labelzoom import LabelZoomClient

with LabelZoomClient() as client:                # anonymous; this works
    result = client.convert(
        "zpl", "png",
        "^XA^FO20,20^A0N,28^FDHello^FS^XZ",
        target_dpi=300, label_width=4, label_height=6,
    )

Path("label.png").write_bytes(result.content)
```

`result.content` is the authoritative payload — the PDF and image targets are binary, and every
printer-language target can inline binary of its own.
`result.text` decodes it using the response charset for the textual ones.

With a key — passed explicitly, or picked up from `LABELZOOM_API_KEY`:

```python
client = LabelZoomClient("lz_live_...")
from_env = LabelZoomClient()                     # reads LABELZOOM_API_KEY
anonymous = LabelZoomClient(None)                # forces anonymous, ignoring the env
```

Omitting the argument and passing `None` mean different things on purpose: omitting it consults
the environment, and `None` (or `""`) suppresses that fallback.

Filling variable fields — **each record produces one label**:

```python
result = client.convert(
    "zpl", "pdf", template,
    data=[{"name": "ACME Corp", "sku": "12345"},
          {"name": "Globex", "sku": "67890"}],
)                                                # a 2-page PDF
```

### Async

`AsyncLabelZoomClient` is a mirror, not a wrapper — same arguments, same behavior, awaited.
The test suite asserts the two signatures are identical, so they cannot drift.

```python
import asyncio
from labelzoom import AsyncLabelZoomClient

async def main() -> None:
    async with AsyncLabelZoomClient() as client:
        result = await client.convert("zpl", "png", zpl, target_dpi=300)
        print(result.status, result.content_type, len(result.content))

asyncio.run(main())
```

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

The newer printer languages (ESC/Label, Fingerprint, EZPL, TPCL) and the design-file formats
(NiceLabel, BarTender, DYMO) are source-only. Send NiceLabel and BarTender files as bytes. CPCL is
a target too, and writes graphics as hex, so its output is plain text.

`jpg` and `url` are **source-only** — `jpg` normalizes to `jpeg`, and `url` is a fetch instruction
rather than a format — and the type system says so:

```python
client.convert("pdf", "url", body)
#                     ^^^^^ error: Argument 2 has incompatible type "Literal['url']";
#                            expected "Literal['zpl', 'epl', 'ipl', 'tspl', ...]"
```

A mypy error, not a runtime 404. Run mypy and you find it before you ship; the SDK also raises
locally rather than round-tripping to the server.

`epl`, `ipl`, `tspl`, `dpl` and `sbpl` are targets as well as sources — ``client.convert("pdf", "epl", body)`` is a real conversion. Their
output is `text/plain` with every label concatenated, but they can all carry raw binary (EPL's `GW`,
TSPL's `BITMAP`, IPL's `STX`/`ETX`-framed bitmap columns, SBPL's inline graphics): read `result.content` rather than `result.text` whenever a label might carry graphics.

`"url"` as the source has the *server* fetch a URL you supply and convert what it finds.
Validate the URL first if it came from untrusted input.

## Options

Options are keyword arguments with the API's nesting flattened by underscores — `label.width`
becomes `label_width`, `pdf.pageNumber` becomes `pdf_page_number`.

| Keyword | Notes |
|---|---|
| `source_dpi` | the resolution the job or image was made for; server default 203 |
| `target_dpi` | the resolution of the output; server default is the source's, else 203. Not for a PDF target |
| `rotation` | must be a multiple of 90; rejected locally otherwise |
| `scaling` | percent, server default 100 |
| `color_mode` | `"BW"`, `"GRAYSCALE"` (default), `"COLOR"` |
| `darkness` | 0–100, server default 70 |
| `position_x`, `position_y` | pixel offset of the extracted region |
| `watermark` | forced on for the free tier regardless |
| `dialect` | e.g. `"moca"`; **paid** |
| `label_width`, `label_height` | **inches**, not dots |
| `pdf_conversion_mode` | `"IMAGE"` (default) or `"NATIVE"` |
| `pdf_page_number` | **0-based**; omit to convert every page |
| `zpl_commands_to_ignore` | e.g. `["^PQ"]` |
| `zpl_image_compression` | `"Z64"` (default) or `"COMPRESSED_HEX"` |
| `zpl_collapse_copies` | image or PDF to ZPL, `IMAGE` mode: each run of identical labels becomes one label with a `^PQ` count |
| `layout_across`, `layout_gap`, `layout_fill` | image or PDF to ZPL, `IMAGE` mode: labels side by side per row; gap in **inches**; fill `"SEQUENTIAL"` (default) or `"REPEAT"` |
| `split_rows`, `split_columns` | image or PDF to ZPL, `IMAGE` mode: cut each page into a grid of labels |
| `split_width`, `split_height` | one label on the page, in **inches**; omit to divide the page equally |
| `split_left`, `split_top`, `split_column_gap`, `split_row_gap` | margins and gaps on the page, in **inches** |
| `split_skip_blank` | leave out labels with nothing printable |
| `label_index` | **0-based**; converts only this label, on every plan |
| `data` | one label per record |
| `extra` | anything not modeled yet; merged in as-is |

Only options you actually set are sent. The SDK never fills in a client-side default, so a
change to a server default reaches you without an SDK upgrade. That is why unset options use an
`UNSET` sentinel rather than `None` — `watermark=False` is a value you can deliberately send.

For building a request programmatically, `ConversionOptions` is the same field set as a frozen
dataclass:

```python
from labelzoom import ConversionOptions

preset = ConversionOptions(target_dpi=300, label_width=4, label_height=6)
result = client.convert("zpl", "png", zpl, options=preset, rotation=90)
```

Keyword arguments passed alongside `options` win.

## Print

Send a document to a printer connected through the LabelZoom Print Agent. The printer's own
configuration sets its language, and LabelZoom converts the document to it, so a PDF prints on a
ZPL printer. There is no target format. Printing needs an API key with the `print` scope.

```python
import time
from pathlib import Path
from labelzoom import LabelZoomClient

with LabelZoomClient("lz_live_...") as client:
    job = client.print(
        printer_id, "pdf", Path("label.pdf").read_bytes(),
        pdf_page_number=0,                       # the same source options as convert()
        copies=2,
        idempotency_key=order_number,
    )                                            # PrintJob(job_id, status, copies, idempotent)

    batch = client.print_template(
        printer_id, template_id,
        [{"orderNumber": "SO-1001", "sku": "A-100"}],
    )                                            # PrintBatch(jobs, remaining, message)

    details = client.jobs.get(job.job_id)
    while details.status not in ("completed", "failed"):
        time.sleep(2)
        details = client.jobs.get(job.job_id)

    quota = client.get_print_quota()             # quota.jobs_today.remaining
```

`AsyncLabelZoomClient` has the same methods, awaited, and `await client.jobs.get(job_id)`.

- `print` takes ZPL, EPL, IPL, TSPL, DPL, SBPL, Fingerprint, CPCL, LabelZoom XML and JSON, PDF,
  and images, typed as `PrintSource`. To print another format, convert it to the printer's
  language first.
- `copies` sends the whole job that many times, from 1 to 9999, so `^PQ3` with `copies=5` prints
  15 labels.
- Every print carries an `Idempotency-Key`: set your own with `idempotency_key` to make a repeated
  call return the original job instead of printing again, or leave it out and the SDK generates
  one per call, so its own retries can't print twice.
- A template batch that runs out of time returns `remaining`, the number of records not yet
  submitted. Send the same call with the same key to submit them.
- The daily job limit raises `PrintQuotaExceededError`, which is never retried because the limit
  resets at 00:00 UTC.

## Errors

Every non-2xx raises a typed error carrying the server's own message, the raw body, and the
`X-LZ-Request-Id` support handle. The body is never discarded.

```python
from labelzoom import ForbiddenError, LabelZoomError

try:
    client.convert("zpl", "json", zpl)
except ForbiddenError as error:
    if error.is_paid_feature:
        # "JSON export is a paid feature" — the most common free-tier failure.
        print(f"{error.message} (request {error.request_id})")
except LabelZoomError as error:
    print(f"{error.status}: {error.message}")
```

`BadRequestError`, `UnauthorizedError`, `ForbiddenError`, `NotFoundError`,
`PayloadTooLargeError`, `UnprocessableError` (422), `RateLimitedError` and `ServerError` all
subclass `LabelZoomError`. `PrintQuotaExceededError` subclasses `RateLimitedError`. Each carries
`code`, the error's stable code such as `agent_update_required`, or `None`; branch on it rather
than on the message.

`LabelZoomValidationError` deliberately does **not** — it means the calling code is wrong, it
never reaches the network, and it should not be swallowed by a handler written for server
failures. It subclasses `ValueError`.

## Retries

429, 5xx and transport failures are retried automatically: 3 attempts, 1s/2s/4s with full
jitter, honoring a longer `Retry-After`. The daily print limit is the exception: its 429 raises
at once. Other 4xx responses raise immediately — a malformed request will not become valid on a
second attempt.

```python
client = LabelZoomClient(max_retries=0, timeout=30.0)
```

## Testing your own code

The transport and the sleep function are both injectable, so retry logic can be tested without
spending the wall-clock time:

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
```

Pass `http_client=httpx.Client(...)` instead to share a connection pool with the rest of your
application. The SDK will not close a client it did not create.

## Development

```sh
pip install -e ".[dev]"
pytest            # offline; no key, no network
mypy
ruff check
```

The test suite runs the shared
[conformance fixtures](https://github.com/labelzoom/labelzoom-sdk/tree/main/conformance) that
every LabelZoom SDK is checked against, executes each one through **both** the sync and async
clients, and asserts it ran all of them. See
[docs/CONFORMANCE.md](https://github.com/labelzoom/labelzoom-sdk/blob/main/docs/CONFORMANCE.md).

## License

MIT — see [LICENSE](https://github.com/labelzoom/labelzoom-sdk/blob/main/LICENSE).
