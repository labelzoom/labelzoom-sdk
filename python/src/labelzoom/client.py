"""The LabelZoom API clients: :class:`LabelZoomClient` and :class:`AsyncLabelZoomClient`."""

from __future__ import annotations

import asyncio
import os
import platform
import random
import re
import time
from collections.abc import Awaitable, Callable, Mapping, Sequence
from dataclasses import dataclass
from email.utils import parsedate_to_datetime
from types import TracebackType
from typing import Any, Final

import httpx

from ._version import __version__
from .errors import (
    LabelZoomError,
    LabelZoomValidationError,
    error_for_status,
    extract_message,
)
from .formats import (
    ColorMode,
    PdfConversionMode,
    SourceFormat,
    TargetFormat,
    ZplImageCompression,
    source_media_type,
    source_wire_token,
    target_wire_token,
    validate_formats,
)
from .options import UNSET, ConversionOptions, DataRecord, _Unset, serialize_options

__all__ = [
    "API_KEY_ENV_VAR",
    "DEFAULT_BASE_URL",
    "AsyncLabelZoomClient",
    "ConversionResult",
    "LabelZoomClient",
]

#: The production API host.
DEFAULT_BASE_URL: Final = "https://api.labelzoom.com"

#: The environment variable consulted when no credential is passed.
API_KEY_ENV_VAR: Final = "LABELZOOM_API_KEY"

_REQUEST_ID_HEADER: Final = "X-LZ-Request-Id"
_CHARSET_PATTERN: Final = re.compile(r"charset=([^;]+)", re.IGNORECASE)

Body = str | bytes | bytearray


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

    def _build_request(
        self,
        http: httpx.Client | httpx.AsyncClient,
        source: SourceFormat,
        target: TargetFormat,
        body: Body,
        as_base64_text: bool,
        options: ConversionOptions | None,
    ) -> httpx.Request:
        validate_formats(source, target)

        content = body.encode("utf-8") if isinstance(body, str) else bytes(body)
        if not content:
            # The gateway rejects a zero-length body with 400; catching it here saves a
            # round trip and gives a clearer message.
            raise LabelZoomValidationError(
                "body",
                "Source body cannot be empty; the API rejects zero-length requests.",
            )

        params = serialize_options(options)

        headers = {
            "Content-Type": "text/plain" if as_base64_text else source_media_type(source),
            # Accept must be */*. The server's `produces` list omits image/gif, image/bmp
            # and image/jpeg, so naming the target's exact media type yields a 406 from
            # content negotiation before the handler ever runs.
            "Accept": "*/*",
            "User-Agent": self._user_agent,
        }
        if self._credential is not None:
            headers["Authorization"] = f"Bearer {self._credential}"

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
        result = client.convert("zpl", "png", zpl, dpi=300)
        Path("label.png").write_bytes(result.content)
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

    def convert(
        self,
        source: SourceFormat,
        target: TargetFormat,
        body: Body,
        *,
        options: ConversionOptions | None = None,
        as_base64_text: bool = False,
        dpi: int | _Unset = UNSET,
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
        extra: Mapping[str, Any] | _Unset = UNSET,
    ) -> ConversionResult:
        """Convert a label from one format to another.

        Only the options you pass are sent; the SDK never supplies a client-side default.

        :param source: The input format. ``"url"`` posts the URL as the body and has the
            *server* fetch it -- validate it first if it came from untrusted input.
        :param target: The output format. ``"epl"``, ``"tspl"`` and ``"dpl"`` are absent by
            design: they are source-only on the server.
        :param body: The document, as ``bytes`` or ``str``.
        :param options: A pre-built :class:`~labelzoom.ConversionOptions`. Keyword arguments
            passed alongside it win.
        :param as_base64_text: Send the body as base64 ``text/plain`` rather than the
            source's own media type.
        :param label_width: **Inches**, not dots.
        :param label_height: **Inches**, not dots.
        :param pdf_page_number: **0-based**. Omit to convert every page.
        :raises LabelZoomValidationError: The request was rejected locally; nothing was sent.
        :raises LabelZoomError: The API returned a non-2xx response.
        """
        merged = (options or ConversionOptions()).merge(
            dpi=dpi,
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
            extra=extra,
        )
        request = self._build_request(self._http, source, target, body, as_base64_text, merged)

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
                return _result_from(response)

            error = _error_from(response)
            if attempt >= attempts or not _is_retryable(response.status_code):
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

    Identical surface and identical wire behaviour; ``convert`` is a coroutine. The two
    signatures are asserted to match in the test suite so they cannot drift.
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

    async def convert(
        self,
        source: SourceFormat,
        target: TargetFormat,
        body: Body,
        *,
        options: ConversionOptions | None = None,
        as_base64_text: bool = False,
        dpi: int | _Unset = UNSET,
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
        extra: Mapping[str, Any] | _Unset = UNSET,
    ) -> ConversionResult:
        """Convert a label from one format to another. See :meth:`LabelZoomClient.convert`."""
        merged = (options or ConversionOptions()).merge(
            dpi=dpi,
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
            extra=extra,
        )
        request = self._build_request(self._http, source, target, body, as_base64_text, merged)

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
                return _result_from(response)

            error = _error_from(response)
            if attempt >= attempts or not _is_retryable(response.status_code):
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


def _error_from(response: httpx.Response) -> LabelZoomError:
    raw_body = _decode(response.content, response.headers.get("content-type"))

    return error_for_status(
        status=response.status_code,
        message=extract_message(raw_body, response.reason_phrase),
        request_id=response.headers.get(_REQUEST_ID_HEADER),
        raw_body=raw_body,
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


def _is_retryable(status: int) -> bool:
    return status == 429 or status >= 500
