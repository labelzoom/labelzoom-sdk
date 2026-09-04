"""Runs the shared conformance fixtures against the Python SDK.

Entirely offline: the transport is an ``httpx.MockTransport``, so this passes identically on
a fork pull request with no secrets. The fixtures are the same ones the .NET, Node and Java
suites run -- see ``docs/CONFORMANCE.md``.

Every case runs **twice**, once through :class:`LabelZoomClient` and once through
:class:`AsyncLabelZoomClient`. The async client is a mirror rather than a wrapper, so nothing
but an actual second execution proves the two agree.
"""

from __future__ import annotations

import asyncio
import base64
import json
import re
from pathlib import Path
from typing import Any

import httpx
import pytest

from labelzoom import (
    AsyncLabelZoomClient,
    BadRequestError,
    ForbiddenError,
    LabelZoomClient,
    LabelZoomError,
    LabelZoomValidationError,
    NotFoundError,
    PayloadTooLargeError,
    RateLimitedError,
    ServerError,
    UnauthorizedError,
)
from labelzoom.options import UNSET

LANGUAGE = "python"


def _find_conformance_root() -> Path:
    for directory in Path(__file__).resolve().parents:
        candidate = directory / "conformance"
        if (candidate / "cases").is_dir():
            return candidate
    raise RuntimeError("Could not locate the conformance/ directory.")


ROOT = _find_conformance_root()


def _read_json(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8"))


SPEC = _read_json(ROOT / "spec.json")
_skips_file = ROOT / "skips" / f"{LANGUAGE}.json"
SKIPS: dict[str, str] = (
    {s["id"]: s["reason"] for s in _read_json(_skips_file)["skips"]}
    if _skips_file.exists()
    else {}
)
ALL_CASE_IDS: list[str] = SPEC["cases"]
EXPECTED_CASE_IDS = [case_id for case_id in ALL_CASE_IDS if case_id not in SKIPS]

EXECUTED: set[str] = set()

ERROR_KINDS: dict[str, type[LabelZoomError]] = {
    "BadRequest": BadRequestError,
    "Unauthorized": UnauthorizedError,
    "Forbidden": ForbiddenError,
    "NotFound": NotFoundError,
    "PayloadTooLarge": PayloadTooLargeError,
    "RateLimited": RateLimitedError,
    "ServerError": ServerError,
}

OK_RESPONSE: dict[str, Any] = {
    "status": 200,
    "headers": {"content-type": "text/plain"},
    "bodyText": "^XA^XZ",
}


# ------------------------------------------------------------------- transport stub


class _Stub:
    """Replays a scripted list of responses and records every request it was sent."""

    def __init__(self, script: list[dict[str, Any]]) -> None:
        self._script = script
        self._index = 0
        self.requests: list[httpx.Request] = []

    def __call__(self, request: httpx.Request) -> httpx.Response:
        self.requests.append(request)

        if self._index >= len(self._script):
            raise AssertionError(
                f"Unexpected request to {request.url}; no more responses are scripted."
            )
        scripted = self._script[self._index]
        self._index += 1

        if "transportError" in scripted:
            raise httpx.ConnectError(scripted["transportError"], request=request)

        if "bodyBase64" in scripted:
            content = base64.b64decode(scripted["bodyBase64"])
        elif "bodyTextRepeat" in scripted:
            fill, count = scripted["bodyTextRepeat"]
            content = (fill * count).encode("utf-8")
        else:
            content = scripted.get("bodyText", "").encode("utf-8")

        return httpx.Response(
            scripted["status"], headers=scripted.get("headers", {}), content=content
        )


# --------------------------------------------------------------------- translation


def _client_kwargs(given: dict[str, Any], default_max_retries: int | None) -> dict[str, Any]:
    client_spec = given.get("client")

    if client_spec is None:
        # A case with no `client` block is not exercising credential resolution. Force
        # anonymous so a developer's real LABELZOOM_API_KEY cannot change the outcome.
        api_key: Any = None
    elif "apiKey" in client_spec:
        # `null` means "no credential"; omitting the key entirely means "read the env".
        api_key = client_spec["apiKey"]
    else:
        api_key = UNSET

    kwargs: dict[str, Any] = {
        "api_key": api_key,
        "max_retries": given.get("maxRetries", default_max_retries),
        # Deterministic backoff: the fixtures assert exact sleep durations.
        "use_jitter": False,
    }
    if client_spec is not None and "baseUrl" in client_spec:
        kwargs["base_url"] = client_spec["baseUrl"]
    if kwargs["max_retries"] is None:
        del kwargs["max_retries"]

    return kwargs


def _convert_kwargs(given: dict[str, Any]) -> dict[str, Any]:
    """Translate the fixture's wire-shaped options into the SDK's flattened keyword form.

    This translation is the only per-language code in a runner, and writing it is what proves
    the Python divergence in API_CONTRACT.md section 9 is skin deep.
    """
    kwargs: dict[str, Any] = {}
    if given.get("sourceEncoding") == "base64text":
        kwargs["as_base64_text"] = True

    for key, value in (given.get("options") or {}).items():
        if key == "position":
            kwargs["position_x"] = value["x"]
            kwargs["position_y"] = value["y"]
        elif key == "label":
            kwargs["label_width"] = value["width"]
            kwargs["label_height"] = value["height"]
        elif key == "pdf":
            if "conversionMode" in value:
                kwargs["pdf_conversion_mode"] = value["conversionMode"]
            if "pageNumber" in value:
                kwargs["pdf_page_number"] = value["pageNumber"]
        elif key == "zpl":
            if "commandsToIgnore" in value:
                kwargs["zpl_commands_to_ignore"] = value["commandsToIgnore"]
            if "imageCompression" in value:
                kwargs["zpl_image_compression"] = value["imageCompression"]
        elif key == "colorMode":
            kwargs["color_mode"] = value
        elif key == "sourceDpi":
            kwargs["source_dpi"] = value
        elif key == "targetDpi":
            kwargs["target_dpi"] = value
        elif key in {"dpi", "rotation", "scaling", "darkness", "watermark", "dialect", "data"}:
            kwargs[key] = value
        else:
            raise AssertionError(
                f"Fixture sets option '{key}', which the Python runner does not map. "
                "Add it to _convert_kwargs rather than skipping the case."
            )

    return kwargs


def _run(
    is_async: bool,
    given: dict[str, Any],
    script: list[dict[str, Any]],
    *,
    default_max_retries: int | None = None,
) -> tuple[Any, BaseException | None, _Stub, list[float]]:
    """Execute one fixture through either client, returning what happened.

    Both branches must stay literally parallel: any asymmetry here would let a sync-only bug
    pass as an async success.
    """
    stub = _Stub(script)
    sleeps: list[float] = []
    kwargs = _client_kwargs(given, default_max_retries)
    call = (given["source"], given["target"], given["bodyText"])
    convert_kwargs = _convert_kwargs(given)

    result: Any = None
    error: BaseException | None = None

    if is_async:

        async def go() -> Any:
            async def record(seconds: float) -> None:
                sleeps.append(seconds)

            async with AsyncLabelZoomClient(
                transport=httpx.MockTransport(stub), sleep=record, **kwargs
            ) as client:
                return await client.convert(*call, **convert_kwargs)

        try:
            result = asyncio.run(go())
        except (LabelZoomError, LabelZoomValidationError, httpx.TransportError) as exc:
            error = exc
    else:
        with LabelZoomClient(
            transport=httpx.MockTransport(stub), sleep=sleeps.append, **kwargs
        ) as client:
            try:
                result = client.convert(*call, **convert_kwargs)
            except (LabelZoomError, LabelZoomValidationError, httpx.TransportError) as exc:
                error = exc

    return result, error, stub, sleeps


# ------------------------------------------------------------------- case runners


def _assert_error(expected: dict[str, Any], actual: BaseException | None) -> None:
    assert isinstance(actual, LabelZoomError), f"expected a LabelZoomError, got {actual!r}"

    if "kind" in expected:
        assert isinstance(actual, ERROR_KINDS[expected["kind"]])
    if "status" in expected:
        assert actual.status == expected["status"]
    if "message" in expected:
        assert actual.message == expected["message"]
    if expected.get("messageNonEmpty"):
        assert actual.message.strip() != ""
    if "messageMaxLength" in expected:
        assert len(actual.message) <= expected["messageMaxLength"]
    if "rawBodyLength" in expected:
        assert actual.raw_body is not None
        assert len(actual.raw_body) == expected["rawBodyLength"]
    if expected.get("rawBodyPresent"):
        assert actual.raw_body
    if "requestId" in expected:
        assert actual.request_id == expected["requestId"]
    if "isPaidFeature" in expected:
        assert isinstance(actual, ForbiddenError)
        assert actual.is_paid_feature == expected["isPaidFeature"]
    if "retryAfterSeconds" in expected:
        assert isinstance(actual, RateLimitedError)
        assert actual.retry_after_seconds == expected["retryAfterSeconds"]


def _run_request_case(is_async: bool, given: dict[str, Any], expect: dict[str, Any]) -> None:
    _result, error, stub, _sleeps = _run(is_async, given, [OK_RESPONSE])
    assert error is None, f"unexpected error: {error!r}"

    request = stub.requests[-1]

    if "method" in expect:
        assert request.method == expect["method"]
    if "url" in expect:
        url = request.url
        assert f"{url.scheme}://{url.netloc.decode()}{url.path}" == expect["url"]
    if "path" in expect:
        assert request.url.path == expect["path"]

    # httpx.Headers is case-insensitive, which is the semantics the fixtures assume.
    # `expect.headers` is a subset assertion; `headersAbsent` is exact.
    for name, value in expect.get("headers", {}).items():
        assert request.headers.get(name) == value, f"header {name}"
    for name in expect.get("headersAbsent", []):
        assert name not in request.headers, f"header {name} must be absent"
    for name, pattern in expect.get("headersMatch", {}).items():
        assert re.search(pattern, request.headers.get(name, "")), f"header {name}"
    for name, pattern in expect.get("headersNotMatch", {}).items():
        assert not re.search(pattern, request.headers.get(name, "")), f"header {name}"

    for name, expected_json in expect.get("queryJson", {}).items():
        raw = request.url.params.get(name)
        assert raw is not None, f"query parameter {name}"
        # Structural, not textual: JSON key order differs per language and percent-encoding
        # differs per stdlib, so comparing encoded strings would be flake by construction.
        assert json.loads(raw) == expected_json

    for name in expect.get("queryAbsent", []):
        assert name not in request.url.params, f"query parameter {name}"

    for name, keys in expect.get("queryJsonAbsentKeys", {}).items():
        actual = json.loads(request.url.params[name])
        for key in keys:
            assert key not in actual, f"{name}.{key} must not be serialized"

    if "bodyText" in expect:
        assert request.content.decode("utf-8") == expect["bodyText"]


def _run_response_case(is_async: bool, given: dict[str, Any], expect: dict[str, Any]) -> None:
    # Response cases queue one response and assert how it maps. Retry is the subject of
    # retry/*, and leaving it on would consume responses that do not exist for the 429 and
    # 5xx cases.
    call = {"source": "zpl", "target": "zpl", "bodyText": "^XA^XZ"}
    result, error, _stub, _sleeps = _run(is_async, call, [given], default_max_retries=0)

    if "error" in expect:
        _assert_error(expect["error"], error)
        return

    assert error is None, f"unexpected error: {error!r}"
    expected = expect["result"]
    if "status" in expected:
        assert result.status == expected["status"]
    if "contentType" in expected:
        assert result.content_type == expected["contentType"]
    if "text" in expected:
        assert result.text == expected["text"]
    if "bytesBase64" in expected:
        assert base64.b64encode(result.content).decode("ascii") == expected["bytesBase64"]
    if "requestId" in expected:
        assert result.request_id == expected["requestId"]


def _run_retry_case(is_async: bool, given: dict[str, Any], expect: dict[str, Any]) -> None:
    call = dict(given, source="zpl", target="zpl", bodyText="^XA^XZ")
    result, error, stub, sleeps = _run(is_async, call, given["responses"])

    if "error" in expect:
        _assert_error(expect["error"], error)
    else:
        assert error is None, f"unexpected error: {error!r}"
        if "text" in expect.get("result", {}):
            assert result.text == expect["result"]["text"]

    assert len(stub.requests) == expect["attempts"], "attempts"
    assert sleeps == expect["sleepsSeconds"]


def _run_validation_case(is_async: bool, given: dict[str, Any], expect: dict[str, Any]) -> None:
    _result, error, stub, _sleeps = _run(is_async, given, [OK_RESPONSE])

    assert isinstance(error, LabelZoomValidationError), (
        f"expected a validation error, got {error!r}"
    )
    assert error.parameter == expect["validationError"]["parameter"]
    # Local validation must never reach the network.
    assert len(stub.requests) == expect["requestsSent"], "requests sent"


# -------------------------------------------------------------------- the suite


@pytest.mark.parametrize("is_async", [False, True], ids=["sync", "async"])
@pytest.mark.parametrize("case_id", EXPECTED_CASE_IDS)
def test_conformance(case_id: str, is_async: bool, monkeypatch: pytest.MonkeyPatch) -> None:
    fixture = _read_json(ROOT / "cases" / f"{case_id}.json")
    given, expect = fixture["given"], fixture["expect"]

    # A developer's real key in the environment must not leak into a case that does not set
    # one, or `auth-absent` passes locally and fails in CI (or worse, the reverse).
    monkeypatch.delenv("LABELZOOM_API_KEY", raising=False)
    for name, value in (given.get("env") or {}).items():
        monkeypatch.setenv(name, value)

    kind = case_id.split("/")[0]
    if kind == "request":
        _run_request_case(is_async, given, expect)
    elif kind == "response":
        _run_response_case(is_async, given, expect)
    elif kind == "retry":
        _run_retry_case(is_async, given, expect)
    elif kind == "validation":
        _run_validation_case(is_async, given, expect)
    else:
        raise AssertionError(f"Unknown case kind for '{case_id}'.")

    EXECUTED.add(case_id)


def test_sync_and_async_convert_signatures_match() -> None:
    """The two clients duplicate a 20-argument signature; this is what keeps them identical.

    Nothing else would catch an option added to the sync client and forgotten on the async
    one -- the conformance cases would keep passing, because the runner only calls what the
    fixtures name.
    """
    import inspect

    sync = inspect.signature(LabelZoomClient.convert)
    asynchronous = inspect.signature(AsyncLabelZoomClient.convert)

    assert list(sync.parameters) == list(asynchronous.parameters)
    for name, parameter in sync.parameters.items():
        other = asynchronous.parameters[name]
        assert parameter.kind == other.kind, name
        assert parameter.default == other.default, name
        assert parameter.annotation == other.annotation, name


def test_covers_every_declared_case() -> None:
    """The whole anti-drift mechanism.

    A suite that quietly runs a subset of the fixtures reports success exactly like one that
    runs all of them, so coverage is asserted rather than assumed. This relies on running
    after the parametrized cases in the same session -- see the pytest note in pyproject.toml.
    """
    for case_id, reason in SKIPS.items():
        assert case_id in ALL_CASE_IDS, (
            f"skips/{LANGUAGE}.json declares unknown case '{case_id}'"
        )
        assert reason.strip(), f"skip '{case_id}' has no reason"

    assert sorted(EXECUTED) == sorted(EXPECTED_CASE_IDS)
