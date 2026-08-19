"""The error taxonomy: one base class for every API error, plus a local validation error."""

from __future__ import annotations

import json

__all__ = [
    "BadRequestError",
    "ForbiddenError",
    "LabelZoomError",
    "LabelZoomValidationError",
    "NotFoundError",
    "PayloadTooLargeError",
    "RateLimitedError",
    "ServerError",
    "UnauthorizedError",
]

MAX_MESSAGE_LENGTH = 512


class LabelZoomError(Exception):
    """Base type for every error the LabelZoom API returns.

    Catch this to handle them all. Note that :class:`LabelZoomValidationError` deliberately
    does *not* inherit from it.
    """

    def __init__(
        self,
        message: str,
        *,
        status: int,
        request_id: str | None = None,
        raw_body: str | None = None,
    ) -> None:
        super().__init__(message)
        self.message = message
        #: The HTTP status code the API returned.
        self.status = status
        #: The ``X-LZ-Request-Id`` response header, if present. Quote this to LabelZoom
        #: support -- it identifies the exact request server-side.
        self.request_id = request_id
        #: The raw response body, untruncated. ``message`` is derived from it and capped at
        #: 512 characters; this is not.
        self.raw_body = raw_body


class BadRequestError(LabelZoomError):
    """HTTP 400. The request was malformed or the conversion path is invalid."""


class UnauthorizedError(LabelZoomError):
    """HTTP 401. The supplied credential was rejected."""


class ForbiddenError(LabelZoomError):
    """HTTP 403. The credential is valid but not entitled to this operation."""

    def __init__(
        self,
        message: str,
        *,
        status: int,
        request_id: str | None = None,
        raw_body: str | None = None,
        is_paid_feature: bool = False,
    ) -> None:
        super().__init__(message, status=status, request_id=request_id, raw_body=raw_body)
        #: True when this 403 is a paywall rather than a permissions problem -- "JSON export
        #: is a paid feature" and friends. By far the most common anonymous-tier failure, so
        #: it gets a flag instead of leaving callers to match strings.
        self.is_paid_feature = is_paid_feature


class NotFoundError(LabelZoomError):
    """HTTP 404. The conversion path does not exist."""


class PayloadTooLargeError(LabelZoomError):
    """HTTP 413. The body exceeded the tier's limit -- 1 MB on the anonymous free tier."""


class RateLimitedError(LabelZoomError):
    """HTTP 429. Too many requests."""

    def __init__(
        self,
        message: str,
        *,
        status: int,
        request_id: str | None = None,
        raw_body: str | None = None,
        retry_after_seconds: float | None = None,
    ) -> None:
        super().__init__(message, status=status, request_id=request_id, raw_body=raw_body)
        #: ``Retry-After`` in seconds, when the server sent one. The SDK already honours this
        #: during its own retries; this exposes it for callers doing their own.
        self.retry_after_seconds = retry_after_seconds


class ServerError(LabelZoomError):
    """HTTP 5xx. Retried automatically before surfacing."""


class LabelZoomValidationError(ValueError):
    """A request was rejected locally, before any network call.

    Deliberately *not* a :class:`LabelZoomError`: this is a bug in the calling code, not a
    server response. It carries no status, it is never retried, and a caller catching
    ``LabelZoomError`` to implement fallback behaviour should not swallow it.
    """

    def __init__(self, parameter: str, message: str) -> None:
        super().__init__(message)
        self.message = message
        #: The conversion parameter at fault, named as it appears on the wire.
        self.parameter = parameter


def extract_message(raw_body: str | None, reason_phrase: str | None) -> str:
    """Pull the human-readable detail out of an error body.

    Both shapes in play put it on ``message``: the gateway returns ``{"message": "..."}`` and
    Spring returns ``{timestamp, status, error, message, path}``. Anything else -- a
    rate-limit body keyed on ``error``, an HTML 502, a truncated JSON fragment -- falls
    through to the raw text, then to the status reason phrase.
    """
    if raw_body is not None and raw_body.strip():
        try:
            parsed = json.loads(raw_body)
        except ValueError:
            # Not JSON, or malformed. The raw body is still the most useful thing available.
            pass
        else:
            if isinstance(parsed, dict):
                message = parsed.get("message")
                if isinstance(message, str) and message.strip():
                    return message[:MAX_MESSAGE_LENGTH]

        return raw_body.strip()[:MAX_MESSAGE_LENGTH]

    if reason_phrase is not None and reason_phrase.strip():
        return reason_phrase

    return "The LabelZoom API returned an error with no response body."


def error_for_status(
    *,
    status: int,
    message: str,
    request_id: str | None = None,
    raw_body: str | None = None,
    retry_after_seconds: float | None = None,
) -> LabelZoomError:
    """Map an HTTP status onto the typed error it becomes (contract rule E4)."""
    if status == 403:
        return ForbiddenError(
            message,
            status=status,
            request_id=request_id,
            raw_body=raw_body,
            # The most common anonymous-tier failure, so it gets a flag rather than leaving
            # every caller to match the string themselves.
            is_paid_feature="paid feature" in message.lower(),
        )
    if status == 429:
        return RateLimitedError(
            message,
            status=status,
            request_id=request_id,
            raw_body=raw_body,
            retry_after_seconds=retry_after_seconds,
        )

    cls: type[LabelZoomError]
    if status == 400:
        cls = BadRequestError
    elif status == 401:
        cls = UnauthorizedError
    elif status == 404:
        cls = NotFoundError
    elif status == 413:
        cls = PayloadTooLargeError
    elif status >= 500:
        cls = ServerError
    else:
        # Anything else non-2xx still becomes a typed error rather than being swallowed --
        # a 406 from content negotiation is the one that actually shows up in practice.
        cls = LabelZoomError

    return cls(message, status=status, request_id=request_id, raw_body=raw_body)
