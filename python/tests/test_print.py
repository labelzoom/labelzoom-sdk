"""The print surface beyond the conformance fixtures.

The fixtures drive print through the same keyword arguments every caller uses, so these tests
pin what they can't: that print offers exactly convert's source options, that the sync and async
signatures agree, and that two calls never share a generated Idempotency-Key.
"""

from __future__ import annotations

import asyncio
import inspect
import json
from collections.abc import Callable

import httpx

from labelzoom import AsyncLabelZoomClient, AsyncPrintJobs, LabelZoomClient, PrintJob, PrintJobs

JOB_RESPONSE = '{"jobId":"j1","status":"queued","copies":2}'

#: Everything convert takes that isn't a source option: the target, and the target stage.
CONVERT_ONLY = {
    "target",
    "options",
    "as_base64_text",
    "target_dpi",
    "rotation",
    "watermark",
    "dialect",
    "zpl_image_compression",
    "zpl_collapse_copies",
    "layout_across",
    "layout_gap",
    "layout_fill",
    "extra",
}

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
            "printer-1",
            "pdf",
            b"%PDF",
            copies=2,
            idempotency_key="order-42",
            pdf_page_number=1,
            split_rows=2,
        )

    (request,) = requests
    assert request.method == "POST"
    assert request.url.path == "/api/v3/printers/printer-1/print"
    assert request.url.params["sourceFormat"] == "pdf"
    assert request.url.params["copies"] == "2"
    assert json.loads(request.url.params["params"]) == {
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
        client.print("printer-1", "zpl", "^XA^XZ")
        client.print("printer-1", "zpl", "^XA^XZ")
        client.print_template("printer-1", "template-1", {"sku": "A-100"})
        client.print_template("printer-1", "template-1", {"sku": "A-100"})

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
            await client.print("printer-1", "zpl", "^XA^XZ")
            await client.print("printer-1", "zpl", "^XA^XZ")

    asyncio.run(go())

    assert len(keys) == 2
    assert keys[0] != keys[1]


def test_ids_are_encoded_as_one_path_segment() -> None:
    requests: list[httpx.Request] = []

    with _recording_client(requests, '{"id":"a/b","status":"queued"}') as client:
        client.jobs.get("a/b")
        client.print_template("printer 1", "t/1")

    assert requests[0].url.raw_path == b"/api/v3/jobs/a%2Fb"
    assert requests[1].url.raw_path == b"/api/v3/printers/printer%201/templates/t%2F1/print"


def test_print_offers_exactly_the_source_options_of_convert() -> None:
    """The Python form of the print-stage shape test (contract section 1.2).

    A source option added to ``convert`` and not to ``print`` fails here, as does a target
    option leaking into ``print``.
    """
    convert = set(inspect.signature(LabelZoomClient.convert).parameters) - {"self"}
    printing = set(inspect.signature(LabelZoomClient.print).parameters) - {"self"}

    assert printing - PRINT_ONLY == convert - CONVERT_ONLY
    assert convert >= CONVERT_ONLY, "CONVERT_ONLY names a parameter convert no longer has"


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
