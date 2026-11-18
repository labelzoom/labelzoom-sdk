"""Cloud Print: the results print calls return, and the validation they share."""

from __future__ import annotations

import re
import uuid
from collections.abc import Mapping
from dataclasses import dataclass
from datetime import datetime, timezone
from typing import Any, Final

from .errors import LabelZoomValidationError

__all__ = [
    "PrintBatch",
    "PrintJob",
    "PrintJobDetails",
    "PrintLimits",
    "PrintQuota",
    "PrintQuotaCounter",
]

_MIN_COPIES: Final = 1
_MAX_COPIES: Final = 9999
_FRACTION: Final = re.compile(r"\.(\d+)")


@dataclass(frozen=True)
class PrintJob:
    """A print job the server accepted."""

    job_id: str
    #: ``dispatched`` when an online agent took it, ``queued`` when none is online. Kept as a
    #: string, because statuses can be added.
    status: str
    #: The copies requested, or ``None`` when the request didn't set any.
    copies: int | None = None
    #: True when the request reused an Idempotency-Key: this is the original job, and nothing
    #: printed again.
    idempotent: bool = False


@dataclass(frozen=True)
class PrintBatch:
    """The jobs a template print created, one per record."""

    jobs: tuple[PrintJob, ...]
    #: Records at the end of the batch that weren't submitted. Send the same request with the
    #: same Idempotency-Key to submit them; records already submitted replay without printing
    #: again.
    remaining: int = 0
    #: The server's explanation when :attr:`remaining` isn't 0, else ``None``.
    message: str | None = None


@dataclass(frozen=True)
class PrintJobDetails:
    """A print job's current state, from ``client.jobs.get(job_id)``."""

    id: str
    #: ``queued``, ``dispatched``, ``printing``, ``completed`` or ``failed``. Statuses can be
    #: added.
    status: str
    printer_id: str | None = None
    agent_id: str | None = None
    #: The format the job was stored in: the printer's language after any conversion.
    source_format: str | None = None
    copies: int | None = None
    #: Why the job failed, or ``None``.
    error: str | None = None
    retry_count: int = 0
    created_date: datetime | None = None
    dispatched_date: datetime | None = None
    completed_date: datetime | None = None
    retried_date: datetime | None = None


@dataclass(frozen=True)
class PrintQuotaCounter:
    """One counted quota."""

    used: int
    limit: int
    remaining: int
    #: Agents only: enrollments not yet claimed.
    pending: int | None = None
    #: Jobs today only: when the count resets, 00:00 UTC.
    reset_at: datetime | None = None


@dataclass(frozen=True)
class PrintLimits:
    """The account's per-request print limits."""

    max_payload_bytes: int
    max_stored_payload_bytes: int
    max_batch_size: int


@dataclass(frozen=True)
class PrintQuota:
    """The account's Cloud Print limits and today's usage, from ``client.get_print_quota()``."""

    plan: str
    agents: PrintQuotaCounter
    printers: PrintQuotaCounter
    #: Resets at ``jobs_today.reset_at``, 00:00 UTC.
    jobs_today: PrintQuotaCounter
    limits: PrintLimits


# ---------------------------------------------------------------------- validation


def require_id(value: str, parameter: str) -> str:
    """A printer, template or job id must be non-empty (contract rule P7)."""
    if not isinstance(value, str) or not value.strip():
        raise LabelZoomValidationError(parameter, f"{parameter} cannot be empty.")
    return value


def validate_copies(copies: int) -> int:
    if (
        not isinstance(copies, int)
        or isinstance(copies, bool)
        or not _MIN_COPIES <= copies <= _MAX_COPIES
    ):
        raise LabelZoomValidationError(
            "copies", f"copies must be a whole number from 1 to 9999, but was {copies!r}."
        )
    return copies


def key_for_call(idempotency_key: str | None) -> str:
    """The Idempotency-Key for one call (contract rule P4).

    It is fixed before the first attempt, so every retry of the call sends the same one, and a
    retry after a lost response can't print the label twice.
    """
    if idempotency_key is None:
        return str(uuid.uuid4())
    if not isinstance(idempotency_key, str) or not idempotency_key.strip():
        raise LabelZoomValidationError(
            "idempotencyKey",
            "An Idempotency-Key cannot be blank. Omit it to have the SDK generate one.",
        )
    return idempotency_key


# ----------------------------------------------------------------------- responses


def read_print_job(json: Mapping[str, Any]) -> PrintJob:
    return PrintJob(
        job_id=_str(json, "jobId") or "",
        status=_str(json, "status") or "",
        copies=_int(json, "copies"),
        idempotent=json.get("idempotent") is True,
    )


def read_print_batch(json: Mapping[str, Any]) -> PrintBatch:
    """One record comes back as a bare job, several as ``{"jobs": [...]}``. Callers get one."""
    jobs = json.get("jobs")
    if not isinstance(jobs, list):
        return PrintBatch(jobs=(read_print_job(json),))
    return PrintBatch(
        jobs=tuple(read_print_job(job) for job in jobs if isinstance(job, Mapping)),
        remaining=_int(json, "remaining") or 0,
        message=_str(json, "message"),
    )


def read_print_job_details(json: Mapping[str, Any]) -> PrintJobDetails:
    """The wire is snake_case."""
    return PrintJobDetails(
        id=_str(json, "id") or "",
        status=_str(json, "status") or "",
        printer_id=_str(json, "printer_id"),
        agent_id=_str(json, "agent_id"),
        source_format=_str(json, "source_format"),
        copies=_int(json, "copies"),
        error=_str(json, "error"),
        retry_count=_int(json, "retry_count") or 0,
        created_date=_instant(json, "created_date"),
        dispatched_date=_instant(json, "dispatched_date"),
        completed_date=_instant(json, "completed_date"),
        retried_date=_instant(json, "retried_date"),
    )


def read_print_quota(json: Mapping[str, Any]) -> PrintQuota:
    limits = _object(json, "limits")
    return PrintQuota(
        plan=_str(json, "plan") or "",
        agents=_counter(_object(json, "agents")),
        printers=_counter(_object(json, "printers")),
        jobs_today=_counter(_object(json, "jobsToday")),
        limits=PrintLimits(
            max_payload_bytes=_int(limits, "maxPayloadBytes") or 0,
            max_stored_payload_bytes=_int(limits, "maxStoredPayloadBytes") or 0,
            max_batch_size=_int(limits, "maxBatchSize") or 0,
        ),
    )


def _counter(json: Mapping[str, Any]) -> PrintQuotaCounter:
    return PrintQuotaCounter(
        used=_int(json, "used") or 0,
        limit=_int(json, "limit") or 0,
        remaining=_int(json, "remaining") or 0,
        pending=_int(json, "pending"),
        reset_at=_instant(json, "resetAt"),
    )


def _str(json: Mapping[str, Any], name: str) -> str | None:
    value = json.get(name)
    return value if isinstance(value, str) else None


def _int(json: Mapping[str, Any], name: str) -> int | None:
    value = json.get(name)
    return value if isinstance(value, int) and not isinstance(value, bool) else None


def _object(json: Mapping[str, Any], name: str) -> Mapping[str, Any]:
    value = json.get(name)
    return value if isinstance(value, Mapping) else {}


def _instant(json: Mapping[str, Any], name: str) -> datetime | None:
    """An ISO 8601 timestamp as an aware ``datetime``, or ``None`` when absent or unreadable.

    Python 3.10's ``fromisoformat`` takes neither a ``Z`` suffix nor a fraction of other than 3
    or 6 digits, and the server sends ``2026-10-11T15:00:00.000Z``, so both are normalized
    first. A timestamp with no offset is taken as UTC.
    """
    text = _str(json, name)
    if text is None:
        return None

    normalized = text[:-1] + "+00:00" if text.endswith(("Z", "z")) else text
    normalized = _FRACTION.sub(
        lambda match: "." + (match.group(1) + "000000")[:6], normalized, count=1
    )
    try:
        when = datetime.fromisoformat(normalized)
    except ValueError:
        return None

    return when if when.tzinfo is not None else when.replace(tzinfo=timezone.utc)
