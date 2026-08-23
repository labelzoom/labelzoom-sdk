![LabelZoom Logo](https://raw.githubusercontent.com/labelzoom/labelzoom-sdk/main/docs/LabelZoom_Logo_f_400px.png)

# LabelZoom Python SDK

Official Python client for the [LabelZoom API](https://api.labelzoom.com). Converts barcode
labels between ZPL, EPL, TSPL, DPL, PDF, LabelZoom XML/JSON, and raster images.

Python 3.10+. Fully typed (`py.typed`), sync and async clients, one dependency
([httpx](https://www.python-httpx.org/)).

## Install

```sh
pip install labelzoom-sdk
```

The distribution is `labelzoom-sdk`; the import is `labelzoom`.

> **Pre-1.0.** The public API is stable in practice and covered by a shared conformance
> suite, but it stays on `0.x` until all seven language SDKs have validated the same
> contract — two contract-level corrections have already come out of that process.

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
        dpi=300, label_width=4, label_height=6,
    )

Path("label.png").write_bytes(result.content)
```

`result.content` is the authoritative payload — five of the eleven targets are binary, and the
`epl`/`tspl` text targets can inline binary of their own.
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

`AsyncLabelZoomClient` is a mirror, not a wrapper — same arguments, same behaviour, awaited.
The test suite asserts the two signatures are identical, so they cannot drift.

```python
import asyncio
from labelzoom import AsyncLabelZoomClient

async def main() -> None:
    async with AsyncLabelZoomClient() as client:
        result = await client.convert("zpl", "png", zpl, dpi=300)
        print(result.status, result.content_type, len(result.content))

asyncio.run(main())
```

## Formats

```python
SourceFormat = Literal["zpl", "epl", "tspl", "dpl", "xml", "json",
                       "pdf", "png", "bmp", "gif", "jpeg", "jpg", "url"]

TargetFormat = Literal["zpl", "epl", "tspl", "dpl", "xml", "json",
                       "pdf", "png", "bmp", "gif", "jpeg"]
```

`jpg` and `url` are **source-only** — `jpg` normalizes to `jpeg`, and `url` is a fetch instruction
rather than a format — and the type system says so:

```python
client.convert("pdf", "url", body)
#                     ^^^^^ error: Argument 2 has incompatible type "Literal['url']";
#                            expected "Literal['zpl', 'epl', 'tspl', 'dpl', ...]"
```

A mypy error, not a runtime 404. Run mypy and you find it before you ship; the SDK also raises
locally rather than round-tripping to the server.

`epl`, `tspl` and `dpl` are targets as well as sources — ``client.convert("pdf", "epl", body)`` is a real conversion. Their
output is `text/plain` with every label concatenated, but EPL's `GW` and TSPL's `BITMAP` commands
inline raw binary: read `result.content` rather than `result.text` whenever a label might carry graphics.

`"url"` as the source has the *server* fetch a URL you supply and convert what it finds.
Validate the URL first if it came from untrusted input.

## Options

Options are keyword arguments with the API's nesting flattened by underscores — `label.width`
becomes `label_width`, `pdf.pageNumber` becomes `pdf_page_number`.

| Keyword | Notes |
|---|---|
| `dpi` | server default 203 |
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
| `data` | one label per record |
| `extra` | anything not modeled yet; merged in as-is |

Only options you actually set are sent. The SDK never fills in a client-side default, so a
change to a server default reaches you without an SDK upgrade. That is why unset options use an
`UNSET` sentinel rather than `None` — `watermark=False` is a value you can deliberately send.

For building a request programmatically, `ConversionOptions` is the same field set as a frozen
dataclass:

```python
from labelzoom import ConversionOptions

preset = ConversionOptions(dpi=300, label_width=4, label_height=6)
result = client.convert("zpl", "png", zpl, options=preset, rotation=90)
```

Keyword arguments passed alongside `options` win.

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
`PayloadTooLargeError`, `RateLimitedError` and `ServerError` all subclass `LabelZoomError`.

`LabelZoomValidationError` deliberately does **not** — it means the calling code is wrong, it
never reaches the network, and it should not be swallowed by a handler written for server
failures. It subclasses `ValueError`.

## Retries

429, 5xx and transport failures are retried automatically: 3 attempts, 1s/2s/4s with full
jitter, honouring a longer `Retry-After`. Other 4xx responses raise immediately — a malformed
request will not become valid on a second attempt.

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
