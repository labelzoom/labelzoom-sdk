"""Conversion options, and the single serialization path that renders them onto the wire."""

from __future__ import annotations

import json
from collections.abc import Mapping, Sequence
from dataclasses import dataclass, fields
from typing import Any, Final, Literal

from .errors import LabelZoomValidationError
from .formats import ColorMode, PdfConversionMode, ZplImageCompression

__all__ = ["UNSET", "ConversionOptions", "DataRecord"]

#: A single label's variable-field values. Each record produces one label.
DataRecord = Mapping[str, Any]


class _Unset:
    """The "caller never set this" sentinel.

    ``None`` cannot do this job: ``watermark=False`` and ``dpi=0`` are meaningful values a
    caller may set deliberately, and contract rule C1 forbids the SDK from emitting anything
    the caller did not ask for. A client-side ``dpi=203`` in a request nobody configured
    would defeat any future change to the server's default.
    """

    _instance: _Unset | None = None

    def __new__(cls) -> _Unset:
        if cls._instance is None:
            cls._instance = super().__new__(cls)
        return cls._instance

    def __bool__(self) -> Literal[False]:
        return False

    def __repr__(self) -> str:
        return "UNSET"


UNSET: Final = _Unset()


@dataclass(frozen=True)
class ConversionOptions:
    """Conversion options in flattened, snake_case form.

    Every field is optional and **only the ones you set are sent**. The SDK never fills in a
    client-side default, so a change to a server default reaches you without an SDK upgrade.

    The nesting the API uses (``label.width``, ``pdf.pageNumber``) is flattened here with
    underscores, which is what reads as Python. The wire shape is reassembled during
    serialization; see :func:`serialize_options`.

    Passing one of these to ``convert(options=...)`` is the escape hatch for building a
    request programmatically. For a literal call site, the keyword arguments on ``convert``
    are the same names and are usually what you want.
    """

    #: How the source's absolute positions are interpreted: dots for printer languages, pixels
    #: for bitmap images, an override of the document's dpi for LabelZoom XML/JSON. Not
    #: applicable to PDF sources (vector).
    source_dpi: int | _Unset = UNSET
    #: The resolution the output is authored at. Applies to printer-language and XML/JSON
    #: targets, and to raster targets when the source is a bitmap or PDF. Server default 203.
    target_dpi: int | _Unset = UNSET
    #: Legacy alias for a single print resolution; the server applies it to whichever side(s)
    #: of the chosen path support a dpi. Prefer source_dpi/target_dpi.
    #:
    #: .. deprecated:: prefer source_dpi or target_dpi.
    dpi: int | _Unset = UNSET
    #: Degrees clockwise. Must be a multiple of 90. Server default 0.
    rotation: int | _Unset = UNSET
    #: Percent. Server default 100.
    scaling: float | _Unset = UNSET
    #: Server default ``GRAYSCALE``.
    color_mode: ColorMode | _Unset = UNSET
    #: Luminance threshold 0-100. Server default 70.
    darkness: int | _Unset = UNSET
    #: Pixel offset of the extracted region.
    position_x: int | _Unset = UNSET
    position_y: int | _Unset = UNSET
    #: Forced on for the anonymous free tier regardless.
    watermark: bool | _Unset = UNSET
    #: e.g. ``"moca"``. Requires a paid license.
    dialect: str | _Unset = UNSET
    #: One entry per output label. A single record is wrapped into a one-element list.
    data: DataRecord | Sequence[DataRecord] | _Unset = UNSET
    #: **Inches**, not dots.
    label_width: float | _Unset = UNSET
    #: **Inches**, not dots.
    label_height: float | _Unset = UNSET
    #: Server default ``IMAGE``. PDF sources only.
    pdf_conversion_mode: PdfConversionMode | _Unset = UNSET
    #: **0-based**. Omit to convert every page.
    pdf_page_number: int | _Unset = UNSET
    #: e.g. ``["^PQ"]``.
    zpl_commands_to_ignore: Sequence[str] | _Unset = UNSET
    #: Server default ``Z64``. ZPL targets only.
    zpl_image_compression: ZplImageCompression | _Unset = UNSET
    #: Anything the SDK does not model yet, merged in at the top level of ``params``.
    #: Unknown keys are ignored server-side, so this is a safe forward-compatibility hatch.
    extra: Mapping[str, Any] | _Unset = UNSET

    def merge(self, **overrides: Any) -> ConversionOptions:
        """Return a copy with the explicitly-set ``overrides`` applied.

        Values that are ``UNSET`` are skipped, so this composes without clobbering.
        """
        merged = {
            field.name: getattr(self, field.name)
            for field in fields(self)
            if not isinstance(getattr(self, field.name), _Unset)
        }
        merged.update({k: v for k, v in overrides.items() if not isinstance(v, _Unset)})
        return ConversionOptions(**merged)


# Flat field name -> path within the wire JSON. The only place the nesting is described.
_WIRE_PATHS: dict[str, tuple[str, ...]] = {
    "source_dpi": ("sourceDpi",),
    "target_dpi": ("targetDpi",),
    "dpi": ("dpi",),
    "rotation": ("rotation",),
    "scaling": ("scaling",),
    "color_mode": ("colorMode",),
    "darkness": ("darkness",),
    "position_x": ("position", "x"),
    "position_y": ("position", "y"),
    "watermark": ("watermark",),
    "dialect": ("dialect",),
    "data": ("data",),
    "label_width": ("label", "width"),
    "label_height": ("label", "height"),
    "pdf_conversion_mode": ("pdf", "conversionMode"),
    "pdf_page_number": ("pdf", "pageNumber"),
    "zpl_commands_to_ignore": ("zpl", "commandsToIgnore"),
    "zpl_image_compression": ("zpl", "imageCompression"),
}


def serialize_options(options: ConversionOptions | None) -> str | None:
    """Validate options locally and render them as the ``params`` JSON value.

    Everything travels in one ``?params=<JSON>`` parameter, never dot-notation. The API
    accepts both and merges them, but dot-notation cannot express every parameter --
    ``?data=[{}]`` is rejected with 400 while ``?params={"data":[{}]}`` succeeds. One
    serialization path, no special cases.

    Returns ``None`` when nothing was set, in which case no query parameter is emitted at all.
    """
    if options is None:
        return None

    params: dict[str, Any] = {}

    for field in fields(options):
        value = getattr(options, field.name)
        if isinstance(value, _Unset):
            continue

        if field.name == "extra":
            params.update(value)
            continue

        if field.name == "rotation":
            value = _validate_rotation(value)
        elif field.name == "darkness":
            value = _validate_darkness(value)
        elif field.name == "data":
            value = _normalize_data(value)
        elif field.name == "zpl_commands_to_ignore":
            value = list(value)

        path = _WIRE_PATHS[field.name]
        target = params
        for segment in path[:-1]:
            target = target.setdefault(segment, {})
        target[path[-1]] = value

    if not params:
        return None

    return json.dumps(params, separators=(",", ":"))


def _validate_rotation(value: Any) -> int:
    # Rejected locally: the server would 400, and this is unambiguously a caller bug.
    if not isinstance(value, int) or isinstance(value, bool) or value % 90 != 0:
        raise LabelZoomValidationError(
            "rotation",
            f"Rotation must be a multiple of 90 degrees, but was {value!r}.",
        )
    return value


def _validate_darkness(value: Any) -> int:
    if not isinstance(value, int) or isinstance(value, bool) or not 0 <= value <= 100:
        raise LabelZoomValidationError(
            "darkness",
            f"Darkness must be between 0 and 100, but was {value!r}.",
        )
    return value


def _normalize_data(value: Any) -> list[DataRecord]:
    """``data`` is always an array -- one entry produces one label.

    A caller passing a single record means "one label", so a bare mapping is wrapped rather
    than rejected.
    """
    records = [value] if isinstance(value, Mapping) else list(value)

    for index, record in enumerate(records):
        if not isinstance(record, Mapping):
            raise LabelZoomValidationError(
                "data",
                f"data[{index}] is {_describe(record)}; every entry must be a mapping whose "
                "keys are the label's variable field names.",
            )

    return records


def _describe(value: Any) -> str:
    if value is None:
        return "None"
    return f"a {type(value).__name__}"
