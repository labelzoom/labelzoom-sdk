"""The print surface beyond the conformance fixtures.

The fixtures drive print through the same keyword arguments every caller uses, so these tests
pin what they can't: that print is convert with the printer in the target's place, taking
exactly convert's source options plus ``rotation``, that the sync and async signatures agree,
and that two calls never share a generated Idempotency-Key.
"""

from __future__ import annotations

import asyncio
import inspect
import json
from collections.abc import Callable

import httpx
import pytest

from labelzoom import (
    AsyncLabelZoomClient,
    AsyncPrintJobs,
    LabelZoomClient,
    LabelZoomValidationError,
    PrintJob,
    PrintJobs,
)

JOB_RESPONSE = '{"jobId":"j1","status":"queued","copies":2}'

#: Convert's target options: what changes the output rather than how the source is read.
TARGET_OPTIONS = {
    "target_dpi",
    "rotation",
    "watermark",
    "dialect",
    "zpl_image_compression",
    "zpl_collapse_copies",
    "layout_across",
    "layout_gap",
    "layout_fill",
}

#: The one target option a print takes: rotation turns the finished label in every language.
PRINTER_TARGET_OPTIONS = {"rotation"}

#: Everything else convert takes that print doesn't: the target itself, the pre-built options
#: escape hatch, base64 text (print reads the document's own bytes) and the raw extra map.
CONVERT_ONLY = {"target", "options", "as_base64_text", "extra"}

#: Everything print takes that convert doesn't.
PRINT_ONLY = {"printer_id", "copies", "idempotency_key"}


def _recording_client(
    requests: list[httpx.Request],
    body: str = JOB_RESPONSE,
) -> LabelZoomClient:
    def handler(request: httpx.Request) -> httpx.Response:
        requests.append(request)
        return httpx.Response(200, headers={"content-type": "application/json"}, text=body)

    return LabelZoomClient("lz_test", max_retries=0, transport=httpx.MockTransport(handler))


def test_print_sends_the_document_with_its_source_options_copies_and_key() -> None:
    requests: list[httpx.Request] = []

    with _recording_client(requests) as client:
        job = client.print(
            "pdf",
            "printer-1",
            b"%PDF",
            copies=2,
            idempotency_key="order-42",
            rotation=90,
            pdf_page_number=1,
            split_rows=2,
        )

    (request,) = requests
    assert request.method == "POST"
    assert request.url.path == "/api/v3/printers/printer-1/print"
    assert request.url.params["sourceFormat"] == "pdf"
    assert request.url.params["copies"] == "2"
    assert json.loads(request.url.params["params"]) == {
        "rotation": 90,
        "pdf": {"pageNumber": 1},
        "split": {"rows": 2},
    }
    assert request.headers["idempotency-key"] == "order-42"
    assert request.headers["content-type"] == "application/pdf"
    assert request.headers["accept"] == "application/json"
    assert request.content == b"%PDF"
    assert job == PrintJob(job_id="j1", status="queued", copies=2, idempotent=False)


def test_each_call_generates_its_own_key() -> None:
    requests: list[httpx.Request] = []

    with _recording_client(requests) as client:
        client.print("zpl", "printer-1", "^XA^XZ")
        client.print("zpl", "printer-1", "^XA^XZ")
        client.print_template("template-1", "printer-1", {"sku": "A-100"})
        client.print_template("template-1", "printer-1", {"sku": "A-100"})

    keys = [request.headers["idempotency-key"] for request in requests]
    # Two calls are two prints. Only a retry within one call may reuse a key.
    assert len(set(keys)) == len(keys)


def test_async_calls_generate_their_own_keys() -> None:
    keys: list[str] = []

    def handler(request: httpx.Request) -> httpx.Response:
        keys.append(request.headers["idempotency-key"])
        return httpx.Response(200, text=JOB_RESPONSE)

    async def go() -> None:
        async with AsyncLabelZoomClient(
            "lz_test", max_retries=0, transport=httpx.MockTransport(handler)
        ) as client:
            await client.print("zpl", "printer-1", "^XA^XZ")
            await client.print("zpl", "printer-1", "^XA^XZ")

    asyncio.run(go())

    assert len(keys) == 2
    assert keys[0] != keys[1]


def test_ids_are_encoded_as_one_path_segment() -> None:
    requests: list[httpx.Request] = []

    with _recording_client(requests, '{"id":"a/b","status":"queued"}') as client:
        client.jobs.get("a/b")
        client.print_template("t/1", "printer 1")

    assert requests[0].url.raw_path == b"/api/v3/jobs/a%2Fb"
    assert requests[1].url.raw_path == b"/api/v3/printers/printer%201/templates/t%2F1/print"


def test_design_file_prints_as_its_own_bytes() -> None:
    requests: list[httpx.Request] = []

    with _recording_client(requests) as client:
        client.print("btw", "printer-1", b"BT-btw")

    (request,) = requests
    assert request.url.params["sourceFormat"] == "btw"
    assert request.headers["content-type"] == "application/octet-stream"
    assert "params" not in request.url.params
    assert request.content == b"BT-btw"


def test_url_is_refused_before_sending() -> None:
    requests: list[httpx.Request] = []

    client = _recording_client(requests)
    with client, pytest.raises(LabelZoomValidationError) as raised:
        client.print("url", "printer-1", "https://example.com/a.zpl")  # type: ignore[arg-type]

    assert raised.value.parameter == "source"
    assert requests == []


def test_rotation_is_validated_as_convert_validates_it() -> None:
    requests: list[httpx.Request] = []

    client = _recording_client(requests)
    with client, pytest.raises(LabelZoomValidationError) as raised:
        client.print("zpl", "printer-1", "^XA^XZ", rotation=45)

    assert raised.value.parameter == "rotation"
    assert requests == []


def test_print_mirrors_convert_with_the_printer_in_the_targets_place() -> None:
    """The Python form of the print-stage shape test (contract section 1.2).

    Print takes convert's positional arguments in convert's order, with ``printer_id`` where
    ``target`` stands, then exactly convert's source options plus ``rotation``. A source option
    added to ``convert`` and not to ``print`` fails here, as does any other target option, or a
    base64-text flag, leaking into ``print``.
    """
    convert_signature = inspect.signature(LabelZoomClient.convert)
    print_signature = inspect.signature(LabelZoomClient.print)
    convert = set(convert_signature.parameters) - {"self"}
    printing = set(print_signature.parameters) - {"self"}

    assert list(convert_signature.parameters)[1:4] == ["source", "target", "body"]
    assert list(print_signature.parameters)[1:4] == ["source", "printer_id", "body"]

    source_options = convert - CONVERT_ONLY - TARGET_OPTIONS
    assert printing - PRINT_ONLY == source_options | PRINTER_TARGET_OPTIONS
    assert "as_base64_text" not in printing
    assert convert >= CONVERT_ONLY | TARGET_OPTIONS, "a set names a parameter convert lacks"


def test_template_print_takes_data_and_validate_but_no_source_options() -> None:
    parameters = list(inspect.signature(LabelZoomClient.print_template).parameters)

    assert parameters == [
        "self",
        "template_id",
        "printer_id",
        "data",
        "validate",
        "copies",
        "idempotency_key",
    ]


def _assert_same_signature(sync: Callable[..., object], other: Callable[..., object]) -> None:
    left = inspect.signature(sync)
    right = inspect.signature(other)

    assert list(left.parameters) == list(right.parameters)
    for name, parameter in left.parameters.items():
        mirrored = right.parameters[name]
        assert parameter.kind == mirrored.kind, name
        assert parameter.default == mirrored.default, name
        assert parameter.annotation == mirrored.annotation, name
    assert left.return_annotation == right.return_annotation


def test_sync_and_async_print_signatures_match() -> None:
    for name in ("print", "print_template", "get_print_quota"):
        _assert_same_signature(
            getattr(LabelZoomClient, name), getattr(AsyncLabelZoomClient, name)
        )
    _assert_same_signature(PrintJobs.get, AsyncPrintJobs.get)
