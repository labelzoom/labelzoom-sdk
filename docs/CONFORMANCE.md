# Writing a conformance runner

Every language SDK in this repository proves it obeys [API_CONTRACT.md](API_CONTRACT.md) by
executing the shared fixtures in [`../conformance/`](../conformance/). This document is what you
follow when adding an eighth language, or when a fixture change breaks your suite.

## Why fixtures rather than "write good tests in each language"

Seven independent implementations of the same wire protocol drift. They drift silently, because
each language's tests are written by whoever wrote that language's client, and they encode that
person's reading of the API. The fixtures are a single reading, checked into one place, that every
suite is forced to agree with.

The mechanism that makes this real is [§4](#4-the-completeness-assertion). Without it, fixtures
become decorative: a suite that quietly runs 30 of 80 cases looks exactly like a suite that passes.

## 1. Layout

```
conformance/
  spec.json              version, defaults, the authoritative case list
  cases/
    request/*.json       an SDK call -> the HTTP request it must produce
    response/*.json      a canned HTTP response -> the result or error it must produce
    retry/*.json         a scripted response sequence -> attempts and sleeps
    validation/*.json    a bad call -> a local validation error, with no HTTP request
    typecheck/*.json     a snippet that must NOT compile (statically typed languages)
  skips/<lang>.json      declared skips, each requiring a reason
  lint.mjs               validates the fixtures themselves
```

## 2. Case anatomy

Every case is `{ id, note, given, expect }`. The `note` states which contract rule the case pins
and why it exists; `lint.mjs` rejects a case without one.

### `request` — assert on the outgoing request

```json
{
  "id": "request/param-label-size",
  "note": "C4: label.width and label.height are INCHES, not dots.",
  "given": {
    "client": { "apiKey": null },
    "source": "zpl", "target": "png", "bodyText": "^XA...^XZ",
    "options": { "label": { "width": 4.0, "height": 6.0 } }
  },
  "expect": {
    "method": "POST",
    "path": "/api/v2/convert/zpl/to/png",
    "headers": { "content-type": "text/plain", "accept": "*/*" },
    "headersAbsent": ["authorization"],
    "headersMatch": { "user-agent": "^labelzoom-[a-z0-9]+-sdk/" },
    "queryJson": { "params": { "label": { "width": 4.0, "height": 6.0 } } },
    "bodyText": "^XA...^XZ"
  }
}
```

`given.options` is expressed in the **wire** shape (nested, camelCase), not in any one language's
ergonomics. Your runner translates it into your SDK's surface — chained `withLabelSize(4, 6)` in
C#, `label_width=4.0, label_height=6.0` in Python, an `Options` struct literal in Go. That
translation layer is the only per-language code in a runner, and writing it is what proves the
divergences in [API_CONTRACT.md §9](API_CONTRACT.md#9-divergences) are only skin deep.

### `response`, `retry`, `validation`, `typecheck`

- **`response`** supplies `given.{status, headers, bodyText|bodyBase64}` and expects either
  `expect.result` or `expect.error`.
- **`retry`** supplies `given.{maxRetries, responses[]}` and expects `attempts`,
  `sleepsSeconds[]`, and a terminal result or error. An entry may be
  `{"transportError": "connect"}` instead of a response.
- **`validation`** expects `expect.validationError.parameter` and asserts
  `expect.requestsSent === 0`.
- **`typecheck`** supplies a `given.snippet` that must fail to compile. Dynamically typed
  languages declare these in `skips/`.

## 3. Assertion semantics — the parts that bite

These are not stylistic. Getting them wrong produces a suite that is green in six languages and
flaky in the seventh.

**Compare `queryJson` structurally, never as a string.** URL-decode the `params` value, parse it as
JSON, and deep-compare. JSON object key order differs per language — Go randomizes map iteration
outright — and percent-encoding differs per standard library (`%2F` vs `/`, `+` vs `%20`). String
comparison here is flake by construction.

**Headers are a case-insensitive map.** Lowercase both sides before comparing. `lint.mjs` enforces
lowercase keys in the fixtures so the intent is unambiguous. Note `expect.headers` is a *subset*
assertion — extra headers your HTTP stack adds are fine — while `headersAbsent` is exact.

**`expect.url` vs `expect.path`.** A case gives one or the other. `path` ignores the host; `url`
asserts the fully resolved URL and is used for the base-URL cases.

**Numbers.** `4.0` must serialize as a JSON number, not `"4.0"`. Compare numerically so `4` and
`4.0` match — languages disagree about trailing zeros.

**`expect.queryJsonAbsentKeys`** asserts that the named keys are *not* present, which is how rule
C1 (no client-side defaults leaking into the request) is tested.

**Sleeps are recorded, not slept.** Inject a fake sleeper (contract rule F4) that appends to a list
and returns immediately, and disable jitter in the test seam. A runner that actually sleeps turns
the retry cases into 10 seconds of CI time per language.

## 4. The completeness assertion

**Every runner must end with:**

```
assert executed_case_ids == spec.cases - declared_skips(lang)
```

An unknown case id in `skips/<lang>.json` is a hard failure, not a warning. So is a case that
exists in `spec.json` but was never executed.

This is the entire anti-drift mechanism. A suite that silently skips what it fails is worse than no
suite, because it reports success.

Skips live in `conformance/skips/<lang>.json` and **require a non-empty `reason`**, enforced by
`lint.mjs`:

```json
{
  "language": "python",
  "skips": [
    { "id": "typecheck/epl-is-not-a-target",
      "reason": "Python has no compile step; SourceFormat/TargetFormat are Literal types checked by mypy in a separate lint job, not by the test suite." }
  ]
}
```

The only legitimate skips today are `typecheck/*` in Python and Ruby.

## 5. Error kinds

`expect.error.kind` is a symbolic name from `spec.json`'s `errorKinds`. Each runner keeps a small
map from that name to its own type:

| kind | C# | TypeScript | Go |
|---|---|---|---|
| `BadRequest` | `BadRequestException` | `BadRequestError` | `*BadRequestError` |
| `RateLimited` | `RateLimitedException` | `RateLimitedError` | `*RateLimitedError` |
| … | … | … | … |

## 6. HTTP stub per language

Never open a socket in the conformance suite.

| Language | Stub |
|---|---|
| C# | `StubHttpMessageHandler : HttpMessageHandler` via the `LabelZoomClient(HttpClient)` ctor |
| TypeScript | `undici` `MockAgent` |
| Java | the internal `HttpTransport` SPI, with a `RecordingTransport` |
| Python | `httpx.MockTransport` |
| PHP | an in-repo `MockHttpClient implements Psr\Http\Client\ClientInterface` |
| Go | `http.Client{Transport: roundTripperFunc(...)}` |
| Ruby | `webmock` |

Five of the seven need a transport seam in the SDK's own design. Add it in the design, not as a
test-only afterthought — rule F4 requires the same thing for the sleeper.

## 7. Adding or changing a case

1. Add `conformance/cases/<kind>/<name>.json` with a `note` citing the rule.
2. Add the id to `spec.json`'s `cases` and bump `caseCount`.
3. Run `node conformance/lint.mjs`.
4. Run **every** language suite. Every workflow includes `conformance/**` in its path filter
   specifically so a fixture change cannot land green against one language.
5. If the change alters a rule rather than adding coverage, update
   [API_CONTRACT.md](API_CONTRACT.md) in the same commit and bump `spec.version`.

Changing a fixture to make a failing SDK pass is backwards. Either the SDK is wrong, or the
contract is — decide which, in the PR description.
