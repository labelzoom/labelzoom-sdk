"""The LabelZoom API clients: :class:`LabelZoomClient` and :class:`AsyncLabelZoomClient`."""

from __future__ import annotations

import asyncio
import json
import os
import platform
import random
import re
import time
from collections.abc import Awaitable, Callable, Mapping, Sequence
from dataclasses import dataclass
from email.utils import parsedate_to_datetime
from types import TracebackType
from typing import Any, Final, TypeVar
from urllib.parse import quote

import httpx

from ._version import __version__
from .errors import (
    LabelZoomError,
    LabelZoomValidationError,
    PrintQuotaExceededError,
    error_for_status,
    extract_code,
    extract_message,
)
from .formats import (
    ColorMode,
    LayoutFill,
    PdfConversionMode,
    PrintSource,
    SourceFormat,
    TargetFormat,
    ZplImageCompression,
    source_media_type,
    source_wire_token,
    target_wire_token,
    validate_formats,
    validate_print_source,
)
from .options import (
    UNSET,
    ConversionOptions,
    DataRecord,
    _normalize_data,
    _Unset,
    serialize_options,
)
from .printing import (
    PrintBatch,
    PrintJob,
    PrintJobDetails,
    PrintQuota,
    key_for_call,
    read_print_batch,
    read_print_job,
    read_print_job_details,
    read_print_quota,
    require_id,
    validate_copies,
)

__all__ = [
    "API_KEY_ENV_VAR",
    "DEFAULT_BASE_URL",
    "AsyncLabelZoomClient",
    "AsyncPrintJobs",
    "ConversionResult",
    "LabelZoomClient",
    "PrintJobs",
]

#: The production API host.
DEFAULT_BASE_URL: Final = "https://api.labelzoom.com"

#: The environment variable consulted when no credential is passed.
API_KEY_ENV_VAR: Final = "LABELZOOM_API_KEY"

_REQUEST_ID_HEADER: Final = "X-LZ-Request-Id"
_CHARSET_PATTERN: Final = re.compile(r"charset=([^;]+)", re.IGNORECASE)
_JSON: Final = "application/json"

Body = str | bytes | bytearray

_T = TypeVar("_T")


@dataclass(frozen=True)
class ConversionResult:
    """The outcome of a successful conversion.

    :attr:`content` is authoritative -- PDF, PNG, BMP, GIF and JPEG targets are binary, so
    returning only a string would silently corrupt five of the eight targets.
    """

    #: The response body, exactly as the server sent it.
    content: bytes
    #: The HTTP status code.
    status: int
    #: The response ``Content-Type``, if the server sent one.
    content_type: str | None = None
    #: ``X-LZ-Request-Id``, when the server sent it. The support handle.
    request_id: str | None = None

    @property
    def text(self) -> str:
        """:attr:`content` decoded with the response charset, defaulting to UTF-8."""
        return _decode(self.content, self.content_type)

    def __len__(self) -> int:
        return len(self.content)


class _BaseClient:
    """Configuration and the pure request/response logic both clients share.

    The two clients differ only in how they send and how they sleep. Everything that decides
    *what* goes on the wire lives here, so the sync and async surfaces cannot drift.
    """

    def __init__(
        self,
        *,
        api_key: str | _Unset | None,
        base_url: str,
        max_retries: int,
        user_agent_suffix: str | None,
        use_jitter: bool,
        runtime: str,
    ) -> None:
        if max_retries < 0:
            raise ValueError("max_retries cannot be negative.")

        self._base_url = base_url.rstrip("/")
        self._credential = _resolve_credential(api_key)
        self._max_retries = max_retries
        self._use_jitter = use_jitter

        # The server parses a "LabelZoomStudio/" User-Agent prefix as a Studio version and
        # silently changes PDF handling for versions <= 1.8.2, so the SDK's own token must
        # come first.
        suffix = (user_agent_suffix or "").strip()
        self._user_agent = f"labelzoom-python-sdk/{__version__} ({runtime})"
        if suffix:
            self._user_agent = f"{self._user_agent} {suffix}"

    @property
    def is_authenticated(self) -> bool:
        """Whether a credential was resolved. ``False`` means anonymous free-tier requests."""
        return self._credential is not None

    def _headers(self, accept: str, extra: Mapping[str, str] | None = None) -> dict[str, str]:
        headers = {**(extra or {}), "Accept": accept, "User-Agent": self._user_agent}
        if self._credential is not None:
            headers["Authorization"] = f"Bearer {self._credential}"
        return headers

    def _convert_request(
        self,
        http: httpx.Client | httpx.AsyncClient,
        source: SourceFormat,
        target: TargetFormat,
        body: Body,
        as_base64_text: bool,
        options: ConversionOptions | None,
    ) -> httpx.Request:
        validate_formats(source, target)

        # The gateway rejects a zero-length body with 400; catching it here saves a round trip
        # and gives a clearer message.
        content = _content(
            body, "Source body cannot be empty; the API rejects zero-length requests."
        )

        params = serialize_options(options)

        # Accept must be */*. The server's `produces` list omits image/gif, image/bmp and
        # image/jpeg, so naming the target's exact media type yields a 406 from content
        # negotiation before the handler ever runs.
        headers = self._headers(
            "*/*",
            {"Content-Type": "text/plain" if as_base64_text else source_media_type(source)},
        )

        url = (
            f"{self._base_url}/api/v2/convert/"
            f"{source_wire_token(source)}/to/{target_wire_token(target)}"
        )

        return http.build_request(
            "POST",
            url,
            # Rule C7: no options means a bare URL, not an empty query string.
            params=None if params is None else {"params": params},
            headers=headers,
            content=content,
        )

    def _print_request(
        self,
        http: httpx.Client | httpx.AsyncClient,
        source: PrintSource,
        printer_id: str,
        body: Body,
        options: ConversionOptions,
        copies: int | None,
        idempotency_key: str | None,
    ) -> httpx.Request:
        printer = require_id(printer_id, "printerId")
        validate_print_source(source)
        content = _content(
            body, "The document cannot be empty; the API rejects an empty print request."
        )

        # The server can't tell ZPL from EPL by Content-Type, so the format always travels.
        query = {"sourceFormat": source_wire_token(source)}
        if copies is not None:
            query["copies"] = str(validate_copies(copies))
        # An empty params would make the server convert a document already in the printer's
        # language, so it is sent only when a source option or rotation was set (contract P2).
        params = serialize_options(options)
        if params is not None:
            query["params"] = params

        headers = self._headers(
            _JSON,
            {
                "Content-Type": source_media_type(source),
                "Idempotency-Key": key_for_call(idempotency_key),
            },
        )

        return http.build_request(
            "POST",
            f"{self._base_url}/api/v3/printers/{_segment(printer)}/print",
            params=query,
            headers=headers,
            content=content,
        )

    def _print_template_request(
        self,
        http: httpx.Client | httpx.AsyncClient,
        template_id: str,
        printer_id: str,
        data: DataRecord | Sequence[DataRecord] | None,
        validate: bool,
        copies: int | None,
        idempotency_key: str | None,
    ) -> httpx.Request:
        printer = require_id(printer_id, "printerId")
        template = require_id(template_id, "templateId")
        records: list[dict[str, Any]] = (
            [] if data is None else [dict(record) for record in _normalize_data(data)]
        )

        query: dict[str, str] = {}
        if copies is not None:
            query["copies"] = str(validate_copies(copies))
        if validate:
            query["validate"] = "true"

        # One record is sent as that object, several as an array, and none as {} (contract P3).
        # A batch sends one key: the server derives each record's key from it.
        payload: object = {} if not records else records[0] if len(records) == 1 else records
        headers = self._headers(
            _JSON, {"Content-Type": _JSON, "Idempotency-Key": key_for_call(idempotency_key)}
        )

        url = (
            f"{self._base_url}/api/v3/printers/{_segment(printer)}"
            f"/templates/{_segment(template)}/print"
        )

        return http.build_request(
            "POST",
            url,
            params=query or None,
            headers=headers,
            content=json.dumps(payload, separators=(",", ":")).encode("utf-8"),
        )

    def _get_request(self, http: httpx.Client | httpx.AsyncClient, path: str) -> httpx.Request:
        # Every print response is JSON, so the convert rule of Accept: */* doesn't apply.
        return http.build_request(
            "GET", f"{self._base_url}{path}", headers=self._headers(_JSON)
        )

    def _next_delay(self, attempt: int, retry_after: float | None) -> float:
        """1s, 2s, 4s with full jitter, overridden by a longer ``Retry-After``."""
        backoff = float(2 ** (attempt - 1))
        delay = backoff * random.random() if self._use_jitter else backoff

        # The server knows better than the backoff curve when it tells us how long to wait.
        if retry_after is not None and retry_after > delay:
            delay = retry_after

        return delay


class LabelZoomClient(_BaseClient):
    """A synchronous LabelZoom API client.

    Authentication is *optional*. Without a credential the API serves a free tier
    (watermarked, first label only, 1 MB cap, no multi-page, JSON target or image-to-image),
    so constructing a client with no key is a supported mode, not an error.

    ```python
    from labelzoom import LabelZoomClient

    with LabelZoomClient() as client:
        result = client.convert("zpl", "png", zpl, target_dpi=300)
        Path("label.png").write_bytes(result.content)

        job = client.print("zpl", printer_id, zpl, copies=2)  # Cloud Print
    ```
    """

    def __init__(
        self,
        api_key: str | _Unset | None = UNSET,
        *,
        base_url: str = DEFAULT_BASE_URL,
        timeout: float | httpx.Timeout | None = 60.0,
        max_retries: int = 2,
        user_agent_suffix: str | None = None,
        http_client: httpx.Client | None = None,
        transport: httpx.BaseTransport | None = None,
        use_jitter: bool = True,
        sleep: Callable[[float], None] = time.sleep,
    ) -> None:
        """
        :param api_key: An ``lz_live_``/``lz_test_`` key or a JWT. Omit the argument entirely
            to read ``LABELZOOM_API_KEY`` from the environment; pass ``None`` or ``""`` to
            force anonymous mode and suppress that fallback. (Omitting and passing ``None``
            differ on purpose -- that is what the ``UNSET`` default is for.)
        :param base_url: Defaults to :data:`DEFAULT_BASE_URL`. A path prefix is preserved,
            so a reverse proxy at ``https://proxy/labelzoom`` works.
        :param timeout: Per-request timeout in seconds. ``None`` disables it. Conversions of
            large multi-page PDFs are the slow case.
        :param max_retries: Retries *after* the initial attempt. ``0`` disables retrying.
        :param http_client: Supply your own ``httpx.Client`` to share a connection pool. The
            SDK will not close a client it did not create.
        :param transport: An ``httpx.BaseTransport`` for the client the SDK creates --
            ``httpx.MockTransport`` in tests.
        :param use_jitter: Full jitter on retry backoff. Turn off for deterministic tests.
        :param sleep: Replaces the delay between retries. Substitute a recording no-op in
            tests rather than waiting out real seconds.
        """
        super().__init__(
            api_key=api_key,
            base_url=base_url,
            max_retries=max_retries,
            user_agent_suffix=user_agent_suffix,
            use_jitter=use_jitter,
            runtime=_runtime_token(),
        )
        self._sleep = sleep
        self._owns_http = http_client is None
        self._http = http_client or httpx.Client(timeout=timeout, transport=transport)
        self._jobs = PrintJobs(self)

    def convert(
        self,
        source: SourceFormat,
        target: TargetFormat,
        body: Body,
        *,
        options: ConversionOptions | None = None,
        as_base64_text: bool = False,
        source_dpi: int | _Unset = UNSET,
        target_dpi: int | _Unset = UNSET,
        rotation: int | _Unset = UNSET,
        scaling: float | _Unset = UNSET,
        color_mode: ColorMode | _Unset = UNSET,
        darkness: int | _Unset = UNSET,
        position_x: int | _Unset = UNSET,
        position_y: int | _Unset = UNSET,
        watermark: bool | _Unset = UNSET,
        dialect: str | _Unset = UNSET,
        data: DataRecord | Sequence[DataRecord] | _Unset = UNSET,
        label_width: float | _Unset = UNSET,
        label_height: float | _Unset = UNSET,
        pdf_conversion_mode: PdfConversionMode | _Unset = UNSET,
        pdf_page_number: int | _Unset = UNSET,
        zpl_commands_to_ignore: Sequence[str] | _Unset = UNSET,
        zpl_image_compression: ZplImageCompression | _Unset = UNSET,
        zpl_collapse_copies: bool | _Unset = UNSET,
        layout_across: int | _Unset = UNSET,
        layout_gap: float | _Unset = UNSET,
        layout_fill: LayoutFill | _Unset = UNSET,
        split_rows: int | _Unset = UNSET,
        split_columns: int | _Unset = UNSET,
        split_width: float | _Unset = UNSET,
        split_height: float | _Unset = UNSET,
        split_left: float | _Unset = UNSET,
        split_top: float | _Unset = UNSET,
        split_column_gap: float | _Unset = UNSET,
        split_row_gap: float | _Unset = UNSET,
        split_skip_blank: bool | _Unset = UNSET,
        label_index: int | _Unset = UNSET,
        extra: Mapping[str, Any] | _Unset = UNSET,
    ) -> ConversionResult:
        """Convert a label from one format to another.

        Only the options you pass are sent; the SDK never supplies a client-side default.

        :param source: The input format. ``"url"`` posts the URL as the body and has the
            *server* fetch it -- validate it first if it came from untrusted input.
        :param target: The output format. ``"jpg"`` and ``"url"`` are absent by design:
            ``"jpg"`` is an input spelling of ``"jpeg"``, and ``"url"`` is a fetch
            instruction rather than a format.
        :param body: The document, as ``bytes`` or ``str``.
        :param options: A pre-built :class:`~labelzoom.ConversionOptions`. Keyword arguments
            passed alongside it win.
        :param as_base64_text: Send the body as base64 ``text/plain`` rather than the
            source's own media type.
        :param label_width: **Inches**, not dots.
        :param label_height: **Inches**, not dots.
        :param pdf_page_number: **0-based**. Omit to convert every page.
        :param layout_gap: **Inches**, not dots.
        :param split_width: The ``split_*`` lengths are **inches** on the source page, from
            its top-left corner. Omit ``split_width`` and ``split_height`` to divide the page
            equally.
        :param label_index: **0-based**. For image or PDF to ZPL it counts the labels after
            ``split``, page by page. Omit to convert every label.
        :raises LabelZoomValidationError: The request was rejected locally; nothing was sent.
        :raises LabelZoomError: The API returned a non-2xx response.
        """
        merged = (options or ConversionOptions()).merge(
            source_dpi=source_dpi,
            target_dpi=target_dpi,
            rotation=rotation,
            scaling=scaling,
            color_mode=color_mode,
            darkness=darkness,
            position_x=position_x,
            position_y=position_y,
            watermark=watermark,
            dialect=dialect,
            data=data,
            label_width=label_width,
            label_height=label_height,
            pdf_conversion_mode=pdf_conversion_mode,
            pdf_page_number=pdf_page_number,
            zpl_commands_to_ignore=zpl_commands_to_ignore,
            zpl_image_compression=zpl_image_compression,
            zpl_collapse_copies=zpl_collapse_copies,
            layout_across=layout_across,
            layout_gap=layout_gap,
            layout_fill=layout_fill,
            split_rows=split_rows,
            split_columns=split_columns,
            split_width=split_width,
            split_height=split_height,
            split_left=split_left,
            split_top=split_top,
            split_column_gap=split_column_gap,
            split_row_gap=split_row_gap,
            split_skip_blank=split_skip_blank,
            label_index=label_index,
            extra=extra,
        )
        request = self._convert_request(
            self._http, source, target, body, as_base64_text, merged
        )
        return self._send(request, _result_from)

    def print(
        self,
        source: PrintSource,
        printer_id: str,
        body: Body,
        *,
        copies: int | None = None,
        idempotency_key: str | None = None,
        rotation: int | _Unset = UNSET,
        source_dpi: int | _Unset = UNSET,
        scaling: float | _Unset = UNSET,
        color_mode: ColorMode | _Unset = UNSET,
        darkness: int | _Unset = UNSET,
        position_x: int | _Unset = UNSET,
        position_y: int | _Unset = UNSET,
        data: DataRecord | Sequence[DataRecord] | _Unset = UNSET,
        label_width: float | _Unset = UNSET,
        label_height: float | _Unset = UNSET,
        pdf_conversion_mode: PdfConversionMode | _Unset = UNSET,
        pdf_page_number: int | _Unset = UNSET,
        zpl_commands_to_ignore: Sequence[str] | _Unset = UNSET,
        split_rows: int | _Unset = UNSET,
        split_columns: int | _Unset = UNSET,
        split_width: float | _Unset = UNSET,
        split_height: float | _Unset = UNSET,
        split_left: float | _Unset = UNSET,
        split_top: float | _Unset = UNSET,
        split_column_gap: float | _Unset = UNSET,
        split_row_gap: float | _Unset = UNSET,
        split_skip_blank: bool | _Unset = UNSET,
        label_index: int | _Unset = UNSET,
    ) -> PrintJob:
        """Print a document on a Cloud Print printer.

        The same call as :meth:`convert`, with the printer in the target's place. The printer's
        configuration picks the output language, and LabelZoom converts the document to it, so
        a PDF prints on a ZPL printer. Printing needs an API key with the ``print`` scope. The
        keyword arguments after ``rotation`` are the source options :meth:`convert` takes, with
        the same meaning.

        :param source: The document's format. Every source format prints except ``"url"``,
            because print doesn't fetch.
        :param printer_id: The printer, as the LabelZoom web app shows it.
        :param body: The document's own bytes, as ``bytes`` or ``str``. Print takes no base64
            text: decode it first.
        :param copies: Sends the whole job this many times, 1 to 9999. It multiplies a quantity
            in the document: ``^PQ3`` with 5 copies prints 15 labels.
        :param idempotency_key: Makes the print safe to repeat: the same key returns the
            original job instead of printing again. Without one, the SDK generates a key per
            call, so its own retries can't print twice.
        :param rotation: Degrees clockwise, a multiple of 90. Turns the finished label, whatever
            the printer's language: the one target option a print takes.
        :raises LabelZoomValidationError: The request was rejected locally; nothing was sent.
        :raises PrintQuotaExceededError: The daily job limit is used up. Never retried.
        :raises LabelZoomError: The API returned a non-2xx response.
        """
        options = ConversionOptions().merge(
            rotation=rotation,
            source_dpi=source_dpi,
            scaling=scaling,
            color_mode=color_mode,
            darkness=darkness,
            position_x=position_x,
            position_y=position_y,
            data=data,
            label_width=label_width,
            label_height=label_height,
            pdf_conversion_mode=pdf_conversion_mode,
            pdf_page_number=pdf_page_number,
            zpl_commands_to_ignore=zpl_commands_to_ignore,
            split_rows=split_rows,
            split_columns=split_columns,
            split_width=split_width,
            split_height=split_height,
            split_left=split_left,
            split_top=split_top,
            split_column_gap=split_column_gap,
            split_row_gap=split_row_gap,
            split_skip_blank=split_skip_blank,
            label_index=label_index,
        )
        request = self._print_request(
            self._http, source, printer_id, body, options, copies, idempotency_key
        )
        return self._send(request, _print_job_from)

    def print_template(
        self,
        template_id: str,
        printer_id: str,
        data: DataRecord | Sequence[DataRecord] | None = None,
        *,
        validate: bool = False,
        copies: int | None = None,
        idempotency_key: str | None = None,
    ) -> PrintBatch:
        """Print a published template, one label per record.

        :param template_id: A template published from LabelZoom Studio or the web designer.
        :param printer_id: The printer, as the LabelZoom web app shows it.
        :param data: One mapping of merge-field values per label. Omit it for a template with
            no merge fields.
        :param validate: Checks every record for missing merge fields before anything prints.
        :param copies: Prints each label this many times, 1 to 9999.
        :param idempotency_key: One key for the whole batch; the server derives each record's
            key from it. If the result's ``remaining`` isn't 0, send the same call with the same
            key to submit the rest.
        :raises LabelZoomValidationError: The request was rejected locally; nothing was sent.
        :raises LabelZoomError: The API returned a non-2xx response.
        """
        request = self._print_template_request(
            self._http, template_id, printer_id, data, validate, copies, idempotency_key
        )
        return self._send(request, _print_batch_from)

    @property
    def jobs(self) -> PrintJobs:
        """Print jobs: ``client.jobs.get(job_id)`` follows one to completion."""
        return self._jobs

    def get_print_quota(self) -> PrintQuota:
        """The account's Cloud Print limits and today's usage.

        Check ``jobs_today.remaining`` to stop before the daily limit's 429.
        """
        request = self._get_request(self._http, "/api/v3/print/quota")
        return self._send(request, _print_quota_from)

    def _get_job(self, job_id: str) -> PrintJobDetails:
        job = require_id(job_id, "jobId")
        request = self._get_request(self._http, f"/api/v3/jobs/{_segment(job)}")
        return self._send(request, _job_details_from)

    def _send(self, request: httpx.Request, read: Callable[[httpx.Response], _T]) -> _T:
        """Send under the retry policy (contract rule F), and read a success with ``read``.

        The request is built once, Idempotency-Key included, so every retry sends the same one.
        """
        attempts = self._max_retries + 1
        for attempt in range(1, attempts + 1):
            try:
                response = self._http.send(request)
            except httpx.TransportError:
                if attempt >= attempts:
                    raise
                self._sleep(self._next_delay(attempt, None))
                continue

            if response.is_success:
                return read(response)

            error = _error_from(response)
            if attempt >= attempts or not _is_retryable(error):
                raise error

            self._sleep(self._next_delay(attempt, _retry_after_seconds(response)))

        raise AssertionError("unreachable: the retry loop always returns or raises")

    def close(self) -> None:
        if self._owns_http:
            self._http.close()

    def __enter__(self) -> LabelZoomClient:
        return self

    def __exit__(
        self,
        exc_type: type[BaseException] | None,
        exc: BaseException | None,
        tb: TracebackType | None,
    ) -> None:
        self.close()


class AsyncLabelZoomClient(_BaseClient):
    """The asyncio mirror of :class:`LabelZoomClient`.

    Identical surface and identical wire behavior; every call is a coroutine. The signatures
    are asserted to match in the test suite so they cannot drift.
    """

    def __init__(
        self,
        api_key: str | _Unset | None = UNSET,
        *,
        base_url: str = DEFAULT_BASE_URL,
        timeout: float | httpx.Timeout | None = 60.0,
        max_retries: int = 2,
        user_agent_suffix: str | None = None,
        http_client: httpx.AsyncClient | None = None,
        transport: httpx.AsyncBaseTransport | None = None,
        use_jitter: bool = True,
        sleep: Callable[[float], Awaitable[None]] = asyncio.sleep,
    ) -> None:
        super().__init__(
            api_key=api_key,
            base_url=base_url,
            max_retries=max_retries,
            user_agent_suffix=user_agent_suffix,
            use_jitter=use_jitter,
            runtime=_runtime_token(),
        )
        self._sleep = sleep
        self._owns_http = http_client is None
        self._http = http_client or httpx.AsyncClient(timeout=timeout, transport=transport)
        self._jobs = AsyncPrintJobs(self)

    async def convert(
        self,
        source: SourceFormat,
        target: TargetFormat,
        body: Body,
        *,
        options: ConversionOptions | None = None,
        as_base64_text: bool = False,
        source_dpi: int | _Unset = UNSET,
        target_dpi: int | _Unset = UNSET,
        rotation: int | _Unset = UNSET,
        scaling: float | _Unset = UNSET,
        color_mode: ColorMode | _Unset = UNSET,
        darkness: int | _Unset = UNSET,
        position_x: int | _Unset = UNSET,
        position_y: int | _Unset = UNSET,
        watermark: bool | _Unset = UNSET,
        dialect: str | _Unset = UNSET,
        data: DataRecord | Sequence[DataRecord] | _Unset = UNSET,
        label_width: float | _Unset = UNSET,
        label_height: float | _Unset = UNSET,
        pdf_conversion_mode: PdfConversionMode | _Unset = UNSET,
        pdf_page_number: int | _Unset = UNSET,
        zpl_commands_to_ignore: Sequence[str] | _Unset = UNSET,
        zpl_image_compression: ZplImageCompression | _Unset = UNSET,
        zpl_collapse_copies: bool | _Unset = UNSET,
        layout_across: int | _Unset = UNSET,
        layout_gap: float | _Unset = UNSET,
        layout_fill: LayoutFill | _Unset = UNSET,
        split_rows: int | _Unset = UNSET,
        split_columns: int | _Unset = UNSET,
        split_width: float | _Unset = UNSET,
        split_height: float | _Unset = UNSET,
        split_left: float | _Unset = UNSET,
        split_top: float | _Unset = UNSET,
        split_column_gap: float | _Unset = UNSET,
        split_row_gap: float | _Unset = UNSET,
        split_skip_blank: bool | _Unset = UNSET,
        label_index: int | _Unset = UNSET,
        extra: Mapping[str, Any] | _Unset = UNSET,
    ) -> ConversionResult:
        """Convert a label from one format to another. See :meth:`LabelZoomClient.convert`."""
        merged = (options or ConversionOptions()).merge(
            source_dpi=source_dpi,
            target_dpi=target_dpi,
            rotation=rotation,
            scaling=scaling,
            color_mode=color_mode,
            darkness=darkness,
            position_x=position_x,
            position_y=position_y,
            watermark=watermark,
            dialect=dialect,
            data=data,
            label_width=label_width,
            label_height=label_height,
            pdf_conversion_mode=pdf_conversion_mode,
            pdf_page_number=pdf_page_number,
            zpl_commands_to_ignore=zpl_commands_to_ignore,
            zpl_image_compression=zpl_image_compression,
            zpl_collapse_copies=zpl_collapse_copies,
            layout_across=layout_across,
            layout_gap=layout_gap,
            layout_fill=layout_fill,
            split_rows=split_rows,
            split_columns=split_columns,
            split_width=split_width,
            split_height=split_height,
            split_left=split_left,
            split_top=split_top,
            split_column_gap=split_column_gap,
            split_row_gap=split_row_gap,
            split_skip_blank=split_skip_blank,
            label_index=label_index,
            extra=extra,
        )
        request = self._convert_request(
            self._http, source, target, body, as_base64_text, merged
        )
        return await self._send(request, _result_from)

    async def print(
        self,
        source: PrintSource,
        printer_id: str,
        body: Body,
        *,
        copies: int | None = None,
        idempotency_key: str | None = None,
        rotation: int | _Unset = UNSET,
        source_dpi: int | _Unset = UNSET,
        scaling: float | _Unset = UNSET,
        color_mode: ColorMode | _Unset = UNSET,
        darkness: int | _Unset = UNSET,
        position_x: int | _Unset = UNSET,
        position_y: int | _Unset = UNSET,
        data: DataRecord | Sequence[DataRecord] | _Unset = UNSET,
        label_width: float | _Unset = UNSET,
        label_height: float | _Unset = UNSET,
        pdf_conversion_mode: PdfConversionMode | _Unset = UNSET,
        pdf_page_number: int | _Unset = UNSET,
        zpl_commands_to_ignore: Sequence[str] | _Unset = UNSET,
        split_rows: int | _Unset = UNSET,
        split_columns: int | _Unset = UNSET,
        split_width: float | _Unset = UNSET,
        split_height: float | _Unset = UNSET,
        split_left: float | _Unset = UNSET,
        split_top: float | _Unset = UNSET,
        split_column_gap: float | _Unset = UNSET,
        split_row_gap: float | _Unset = UNSET,
        split_skip_blank: bool | _Unset = UNSET,
        label_index: int | _Unset = UNSET,
    ) -> PrintJob:
        """Print a document. See :meth:`LabelZoomClient.print`."""
        options = ConversionOptions().merge(
            rotation=rotation,
            source_dpi=source_dpi,
            scaling=scaling,
            color_mode=color_mode,
            darkness=darkness,
            position_x=position_x,
            position_y=position_y,
            data=data,
            label_width=label_width,
            label_height=label_height,
            pdf_conversion_mode=pdf_conversion_mode,
            pdf_page_number=pdf_page_number,
            zpl_commands_to_ignore=zpl_commands_to_ignore,
            split_rows=split_rows,
            split_columns=split_columns,
            split_width=split_width,
            split_height=split_height,
            split_left=split_left,
            split_top=split_top,
            split_column_gap=split_column_gap,
            split_row_gap=split_row_gap,
            split_skip_blank=split_skip_blank,
            label_index=label_index,
        )
        request = self._print_request(
            self._http, source, printer_id, body, options, copies, idempotency_key
        )
        return await self._send(request, _print_job_from)

    async def print_template(
        self,
        template_id: str,
        printer_id: str,
        data: DataRecord | Sequence[DataRecord] | None = None,
        *,
        validate: bool = False,
        copies: int | None = None,
        idempotency_key: str | None = None,
    ) -> PrintBatch:
        """Print a published template. See :meth:`LabelZoomClient.print_template`."""
        request = self._print_template_request(
            self._http, template_id, printer_id, data, validate, copies, idempotency_key
        )
        return await self._send(request, _print_batch_from)

    @property
    def jobs(self) -> AsyncPrintJobs:
        """Print jobs: ``client.jobs.get(job_id)`` follows one to completion."""
        return self._jobs

    async def get_print_quota(self) -> PrintQuota:
        """The account's Cloud Print limits and today's usage."""
        request = self._get_request(self._http, "/api/v3/print/quota")
        return await self._send(request, _print_quota_from)

    async def _get_job(self, job_id: str) -> PrintJobDetails:
        job = require_id(job_id, "jobId")
        request = self._get_request(self._http, f"/api/v3/jobs/{_segment(job)}")
        return await self._send(request, _job_details_from)

    async def _send(self, request: httpx.Request, read: Callable[[httpx.Response], _T]) -> _T:
        """Send under the retry policy (contract rule F), and read a success with ``read``.

        The request is built once, Idempotency-Key included, so every retry sends the same one.
        """
        attempts = self._max_retries + 1
        for attempt in range(1, attempts + 1):
            try:
                response = await self._http.send(request)
            except httpx.TransportError:
                if attempt >= attempts:
                    raise
                await self._sleep(self._next_delay(attempt, None))
                continue

            if response.is_success:
                return read(response)

            error = _error_from(response)
            if attempt >= attempts or not _is_retryable(error):
                raise error

            await self._sleep(self._next_delay(attempt, _retry_after_seconds(response)))

        raise AssertionError("unreachable: the retry loop always returns or raises")

    async def aclose(self) -> None:
        if self._owns_http:
            await self._http.aclose()

    async def __aenter__(self) -> AsyncLabelZoomClient:
        return self

    async def __aexit__(
        self,
        exc_type: type[BaseException] | None,
        exc: BaseException | None,
        tb: TracebackType | None,
    ) -> None:
        await self.aclose()


class PrintJobs:
    """Print jobs, from :attr:`LabelZoomClient.jobs`."""

    def __init__(self, client: LabelZoomClient) -> None:
        self._client = client

    def get(self, job_id: str) -> PrintJobDetails:
        """A job's current state. Poll it to follow a job to ``completed`` or ``failed``."""
        return self._client._get_job(job_id)


class AsyncPrintJobs:
    """Print jobs, from :attr:`AsyncLabelZoomClient.jobs`."""

    def __init__(self, client: AsyncLabelZoomClient) -> None:
        self._client = client

    async def get(self, job_id: str) -> PrintJobDetails:
        """A job's current state. See :meth:`PrintJobs.get`."""
        return await self._client._get_job(job_id)


def _resolve_credential(api_key: str | _Unset | None) -> str | None:
    if isinstance(api_key, _Unset):
        return os.environ.get(API_KEY_ENV_VAR) or None
    # An explicit None or "" forces anonymous and must not fall back to the environment.
    return api_key or None


def _runtime_token() -> str:
    return f"python/{platform.python_version()}"


def _result_from(response: httpx.Response) -> ConversionResult:
    return ConversionResult(
        content=response.content,
        status=response.status_code,
        content_type=response.headers.get("content-type"),
        # httpx headers are case-insensitive, which matters here: the gateway sets
        # X-LZ-Request-Id but CORS exposes it as X-LZ-Request-ID.
        request_id=response.headers.get(_REQUEST_ID_HEADER),
    )


def _print_job_from(response: httpx.Response) -> PrintJob:
    return read_print_job(_json_object(response))


def _print_batch_from(response: httpx.Response) -> PrintBatch:
    return read_print_batch(_json_object(response))


def _job_details_from(response: httpx.Response) -> PrintJobDetails:
    return read_print_job_details(_json_object(response))


def _print_quota_from(response: httpx.Response) -> PrintQuota:
    return read_print_quota(_json_object(response))


def _error_from(response: httpx.Response) -> LabelZoomError:
    raw_body = _decode(response.content, response.headers.get("content-type"))

    return error_for_status(
        status=response.status_code,
        message=extract_message(raw_body, response.reason_phrase),
        request_id=response.headers.get(_REQUEST_ID_HEADER),
        raw_body=raw_body,
        code=extract_code(raw_body),
        retry_after_seconds=_retry_after_seconds(response),
    )


def _decode(content: bytes, content_type: str | None) -> str:
    match = _CHARSET_PATTERN.search(content_type or "")
    charset = match.group(1).strip().strip('"') if match else "utf-8"
    try:
        return content.decode(charset)
    except (LookupError, UnicodeDecodeError):
        # An unrecognized or mis-declared charset is not worth failing an otherwise good
        # conversion over.
        return content.decode("utf-8", errors="replace")


def _retry_after_seconds(response: httpx.Response) -> float | None:
    header = response.headers.get("retry-after")
    if header is None:
        return None

    try:
        return float(header)
    except ValueError:
        pass

    try:
        when = parsedate_to_datetime(header)
    except (TypeError, ValueError):
        return None

    return max(0.0, (when.timestamp() - time.time()))


def _is_retryable(error: LabelZoomError) -> bool:
    # F5: the daily print limit's Retry-After is the 00:00 UTC reset, hours away.
    if isinstance(error, PrintQuotaExceededError):
        return False
    return error.status == 429 or error.status >= 500


def _content(body: Body, empty_message: str) -> bytes:
    content = body.encode("utf-8") if isinstance(body, str) else bytes(body)
    if not content:
        raise LabelZoomValidationError("body", empty_message)
    return content


def _segment(value: str) -> str:
    """Percent-encode an id as one path segment (contract rule P1)."""
    return quote(value, safe="")


def _json_object(response: httpx.Response) -> dict[str, Any]:
    parsed = json.loads(_decode(response.content, response.headers.get("content-type")))
    return parsed if isinstance(parsed, dict) else {}
