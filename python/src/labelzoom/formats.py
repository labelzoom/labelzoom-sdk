"""Source and target formats, and the one table that maps them to media types."""

from __future__ import annotations

from typing import Literal, get_args

from .errors import LabelZoomValidationError

#: Formats the LabelZoom API can convert *from*.
#:
#: A different type from :data:`TargetFormat` on purpose. That is what makes ``epl``, ``tspl``
#: and ``dpl`` un-selectable as conversion targets: they are source-only on the server, and
#: they are simply not members of ``TargetFormat``. ``mypy`` rejects the call; there is no
#: round trip to find out.
SourceFormat = Literal[
    "zpl",
    "epl",
    "tspl",
    "dpl",
    "xml",
    "json",
    "pdf",
    "png",
    "bmp",
    "gif",
    "jpeg",
    "jpg",
    # A URL, sent as the request body. The *server* then fetches it and converts what it
    # finds -- a server-side fetch of a URL you supply, so validate it first if it came from
    # untrusted input.
    "url",
]

#: Formats the LabelZoom API can convert *to*.
#:
#: ``epl``, ``tspl`` and ``dpl`` are intentionally absent: the server accepts them as sources
#: only, so ``target="epl"`` is a type error rather than a runtime 404.
TargetFormat = Literal["zpl", "xml", "json", "pdf", "png", "bmp", "gif", "jpeg"]

SOURCE_FORMATS: tuple[SourceFormat, ...] = get_args(SourceFormat)
TARGET_FORMATS: tuple[TargetFormat, ...] = get_args(TargetFormat)

#: Colour handling when rasterizing or tracing images.
ColorMode = Literal["BW", "GRAYSCALE", "COLOR"]

#: How a source PDF is interpreted.
PdfConversionMode = Literal["IMAGE", "NATIVE"]

#: Image compression used when writing ZPL.
ZplImageCompression = Literal["Z64", "COMPRESSED_HEX"]

# The one place in the SDK that knows the format matrix.
#
# The superseded .NET builder hierarchy spread this across seven classes and they drifted:
# one handled 2 of 12 sources, another returned the source content type as the target format.
# Keep it here.
_SOURCE_MEDIA_TYPES: dict[str, str] = {
    "zpl": "text/plain",
    "epl": "text/plain",
    "tspl": "text/plain",
    "dpl": "text/plain",
    "xml": "application/xml",
    "json": "application/json",
    "pdf": "application/pdf",
    "png": "image/png",
    "bmp": "image/bmp",
    "gif": "image/gif",
    "jpeg": "image/jpeg",
    "jpg": "image/jpeg",
    "url": "text/plain",
}


def source_wire_token(source: SourceFormat) -> str:
    """``jpg`` is an alias callers use for files they already have.

    The wire spelling is ``jpeg``.
    """
    return "jpeg" if source == "jpg" else source


def target_wire_token(target: TargetFormat) -> str:
    return target


def source_media_type(source: SourceFormat) -> str:
    return _SOURCE_MEDIA_TYPES[source]


def validate_formats(source: str, target: str) -> None:
    """Reject unknown formats locally.

    ``mypy`` already rejects them, but the SDK cannot assume the caller runs it. Failing here
    turns a 404 round trip into an immediate error that names the valid values -- the runtime
    stand-in for the compile-time guarantee the statically typed SDKs get for free.
    """
    if source not in _SOURCE_MEDIA_TYPES:
        raise LabelZoomValidationError(
            "source",
            f"{source!r} is not a source format. Valid values: "
            + ", ".join(repr(f) for f in SOURCE_FORMATS)
            + ".",
        )
    if target not in TARGET_FORMATS:
        extra = (
            " Note that 'epl', 'tspl' and 'dpl' are source-only formats."
            if target in {"epl", "tspl", "dpl"}
            else ""
        )
        raise LabelZoomValidationError(
            "target",
            f"{target!r} is not a target format. Valid values: "
            + ", ".join(repr(f) for f in TARGET_FORMATS)
            + f".{extra}",
        )
