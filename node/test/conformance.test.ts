/**
 * Runs the shared conformance fixtures against the Node SDK.
 *
 * Entirely offline: `fetch` is replaced with a recording stub, so this passes identically on a
 * fork pull request with no secrets. The fixtures are the same ones the .NET suite runs — see
 * docs/CONFORMANCE.md.
 */
import { readFileSync, existsSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it, afterEach } from 'vitest';

import {
  LabelZoomClient,
  LabelZoomError,
  LabelZoomValidationError,
  BadRequestError,
  UnauthorizedError,
  ForbiddenError,
  NotFoundError,
  PayloadTooLargeError,
  RateLimitedError,
  ServerError,
  TARGET_FORMATS,
  type ConversionOptions,
  type ConversionResult,
  type SourceFormat,
  type TargetFormat,
} from '../src/index.js';

const LANGUAGE = 'node';
const ROOT = findConformanceRoot();

// ---------------------------------------------------------------- fixture loading

function findConformanceRoot(): string {
  let directory = dirname(fileURLToPath(import.meta.url));
  for (let i = 0; i < 6; i++) {
    const candidate = join(directory, 'conformance');
    if (existsSync(join(candidate, 'cases'))) return candidate;
    directory = dirname(directory);
  }
  throw new Error('Could not locate the conformance/ directory.');
}

const readJson = (path: string): any => JSON.parse(readFileSync(path, 'utf8'));

const spec = readJson(join(ROOT, 'spec.json'));
const skipsFile = join(ROOT, 'skips', `${LANGUAGE}.json`);
const skips: Record<string, string> = Object.fromEntries(
  (existsSync(skipsFile) ? readJson(skipsFile).skips : []).map(
    (s: { id: string; reason: string }) => [s.id, s.reason],
  ),
);
const allCaseIds: string[] = spec.cases;
const expectedCaseIds = allCaseIds.filter((id) => !(id in skips));

// ------------------------------------------------------------------- fetch stub

interface RecordedRequest {
  method: string;
  url: URL;
  headers: Record<string, string>;
  bodyText: string;
}

interface ScriptedResponse {
  status: number;
  headers?: Record<string, string>;
  bodyText?: string;
  bodyBase64?: string;
  bodyTextRepeat?: [string, number];
  transportError?: string;
}

function stubFetch(script: ScriptedResponse[]): {
  fetch: typeof globalThis.fetch;
  requests: RecordedRequest[];
} {
  const requests: RecordedRequest[] = [];
  let index = 0;

  const fetchImpl = (async (input: string | URL, init?: RequestInit) => {
    const headers: Record<string, string> = {};
    for (const [key, value] of Object.entries((init?.headers ?? {}) as Record<string, string>)) {
      headers[key.toLowerCase()] = value;
    }

    const rawBody = init?.body;
    const bodyText =
      rawBody instanceof ArrayBuffer
        ? new TextDecoder().decode(new Uint8Array(rawBody))
        : typeof rawBody === 'string'
          ? rawBody
          : '';

    requests.push({ method: init?.method ?? 'GET', url: new URL(String(input)), headers, bodyText });

    const scripted = script[index++];
    if (scripted === undefined) {
      throw new Error(`Unexpected request to ${String(input)}; no more responses are scripted.`);
    }
    if (scripted.transportError !== undefined) {
      throw new TypeError(`fetch failed: ${scripted.transportError}`);
    }

    const body =
      scripted.bodyBase64 !== undefined
        ? Buffer.from(scripted.bodyBase64, 'base64')
        : scripted.bodyTextRepeat !== undefined
          ? scripted.bodyTextRepeat[0].repeat(scripted.bodyTextRepeat[1])
          : (scripted.bodyText ?? '');

    return new Response(body, { status: scripted.status, headers: scripted.headers ?? {} });
  }) as unknown as typeof globalThis.fetch;

  return { fetch: fetchImpl, requests };
}

// ------------------------------------------------------------------ translation

/** Translates the fixture's wire-shaped options into fluent SDK calls. */
function buildRequest(client: LabelZoomClient, given: any) {
  const source = given.source as SourceFormat;
  const target = given.target as TargetFormat;
  const body = given.bodyText as string;

  const sourceBuilder =
    given.sourceEncoding === 'base64text'
      ? client.convert().fromBase64Text(source, body)
      : client.convert().from(source, body);
  const builder = sourceBuilder.to(target);

  const options: ConversionOptions | undefined = given.options;
  if (options === undefined) return builder;

  for (const [key, value] of Object.entries(options)) {
    switch (key) {
      case 'sourceDpi': sourceBuilder.withDpi(value as number); break;
      case 'targetDpi':
      case 'dpi': builder.withDpi(value as number); break;
      case 'rotation': builder.withRotation(value as number); break;
      case 'scaling': builder.withScaling(value as number); break;
      case 'colorMode': builder.withColorMode(value as any); break;
      case 'darkness': builder.withDarkness(value as number); break;
      case 'watermark': builder.withWatermark(value as boolean); break;
      case 'dialect': builder.withDialect(value as string); break;
      case 'position': {
        const p = value as { x: number; y: number };
        builder.withPosition(p.x, p.y);
        break;
      }
      case 'label': {
        const l = value as { width: number; height: number };
        builder.withLabelSize(l.width, l.height);
        break;
      }
      case 'pdf': {
        const p = value as { conversionMode?: any; pageNumber?: number };
        if (p.conversionMode !== undefined) builder.withPdfConversionMode(p.conversionMode);
        if (p.pageNumber !== undefined) builder.withPdfPage(p.pageNumber);
        break;
      }
      case 'zpl': {
        const z = value as { commandsToIgnore?: string[]; imageCompression?: any };
        if (z.commandsToIgnore !== undefined) builder.withZplCommandsToIgnore(...z.commandsToIgnore);
        if (z.imageCompression !== undefined) builder.withZplImageCompression(z.imageCompression);
        break;
      }
      case 'data':
        // A bare object means one label, so it is wrapped rather than rejected.
        builder.withData(...(Array.isArray(value) ? value : [value]));
        break;
      default:
        throw new Error(
          `Fixture sets option '${key}', which the Node runner does not map. ` +
            'Add it to buildRequest rather than skipping the case.',
        );
    }
  }

  return builder;
}

function newClient(
  given: any,
  fetchImpl: typeof globalThis.fetch,
  onSleep?: (ms: number) => void,
  defaultMaxRetries?: number,
): LabelZoomClient {
  const clientOptions = given.client ?? {};
  return new LabelZoomClient({
    // `apiKey: null` in a fixture means "no credential"; undefined would mean "read the env".
    apiKey: 'apiKey' in clientOptions ? (clientOptions.apiKey ?? '') : ('client' in given ? undefined : ''),
    baseUrl: clientOptions.baseUrl,
    maxRetries: given.maxRetries ?? defaultMaxRetries,
    fetch: fetchImpl,
    // Deterministic backoff: the fixtures assert exact sleep durations.
    useJitter: false,
    sleep: async (ms) => { onSleep?.(ms); },
  });
}

const ERROR_KINDS: Record<string, new (...args: any[]) => LabelZoomError> = {
  BadRequest: BadRequestError,
  Unauthorized: UnauthorizedError,
  Forbidden: ForbiddenError,
  NotFound: NotFoundError,
  PayloadTooLarge: PayloadTooLargeError,
  RateLimited: RateLimitedError,
  ServerError: ServerError,
};

function assertError(expected: any, actual: LabelZoomError): void {
  if (expected.kind !== undefined) expect(actual).toBeInstanceOf(ERROR_KINDS[expected.kind]);
  if (expected.status !== undefined) expect(actual.status).toBe(expected.status);
  if (expected.message !== undefined) expect(actual.message).toBe(expected.message);
  if (expected.messageNonEmpty === true) expect(actual.message.trim()).not.toBe('');
  if (expected.messageMaxLength !== undefined) {
    expect(actual.message.length).toBeLessThanOrEqual(expected.messageMaxLength);
  }
  if (expected.rawBodyLength !== undefined) expect(actual.rawBody?.length).toBe(expected.rawBodyLength);
  if (expected.rawBodyPresent === true) expect(actual.rawBody).toBeTruthy();
  if (expected.requestId !== undefined) expect(actual.requestId).toBe(expected.requestId);
  if (expected.isPaidFeature !== undefined) {
    expect((actual as ForbiddenError).isPaidFeature).toBe(expected.isPaidFeature);
  }
  if (expected.retryAfterSeconds !== undefined) {
    expect((actual as RateLimitedError).retryAfterSeconds).toBe(expected.retryAfterSeconds);
  }
}

// ----------------------------------------------------------------- case runners

const OK_RESPONSE: ScriptedResponse = {
  status: 200,
  headers: { 'content-type': 'text/plain' },
  bodyText: '^XA^XZ',
};

async function runRequestCase(given: any, expect_: any): Promise<void> {
  const { fetch, requests } = stubFetch([OK_RESPONSE]);
  const client = newClient(given, fetch);

  await buildRequest(client, given).execute();

  const request = requests[requests.length - 1]!;

  if (expect_.method !== undefined) expect(request.method).toBe(expect_.method);
  if (expect_.url !== undefined) expect(request.url.origin + request.url.pathname).toBe(expect_.url);
  if (expect_.path !== undefined) expect(request.url.pathname).toBe(expect_.path);

  for (const [name, value] of Object.entries(expect_.headers ?? {})) {
    expect(request.headers[name.toLowerCase()], `header ${name}`).toBe(value);
  }
  for (const name of expect_.headersAbsent ?? []) {
    expect(request.headers[name.toLowerCase()], `header ${name} must be absent`).toBeUndefined();
  }
  for (const [name, pattern] of Object.entries(expect_.headersMatch ?? {})) {
    expect(request.headers[name.toLowerCase()] ?? '').toMatch(new RegExp(pattern as string));
  }
  for (const [name, pattern] of Object.entries(expect_.headersNotMatch ?? {})) {
    expect(request.headers[name.toLowerCase()] ?? '').not.toMatch(new RegExp(pattern as string));
  }

  for (const [name, expectedJson] of Object.entries(expect_.queryJson ?? {})) {
    const raw = request.url.searchParams.get(name);
    expect(raw, `query parameter ${name}`).not.toBeNull();
    // Structural, not textual: JSON key order differs per language and percent-encoding
    // differs per stdlib, so comparing encoded strings would be flake by construction.
    expect(JSON.parse(raw!)).toEqual(expectedJson);
  }

  for (const name of expect_.queryAbsent ?? []) {
    expect(request.url.searchParams.get(name), `query parameter ${name}`).toBeNull();
  }

  for (const [name, keys] of Object.entries(expect_.queryJsonAbsentKeys ?? {})) {
    const actual = JSON.parse(request.url.searchParams.get(name)!);
    for (const key of keys as string[]) {
      expect(Object.hasOwn(actual, key), `${name}.${key} must not be serialized`).toBe(false);
    }
  }

  if (expect_.bodyText !== undefined) expect(request.bodyText).toBe(expect_.bodyText);
}

async function runResponseCase(given: any, expect_: any): Promise<void> {
  const { fetch } = stubFetch([given]);
  // Response cases queue one response and assert how it maps. Retry is the subject of retry/*,
  // and leaving it on would consume responses that do not exist for the 429 and 5xx cases.
  const client = newClient(given, fetch, undefined, 0);

  const call = client.convert().fromZpl('^XA^XZ').toZpl().execute();

  if (expect_.error !== undefined) {
    const error = await call.then(
      () => { throw new Error('Expected the call to reject.'); },
      (e: unknown) => e as LabelZoomError,
    );
    expect(error).toBeInstanceOf(LabelZoomError);
    assertError(expect_.error, error);
    return;
  }

  const result: ConversionResult = await call;
  const r = expect_.result;
  if (r.status !== undefined) expect(result.status).toBe(r.status);
  if (r.contentType !== undefined) expect(result.contentType).toBe(r.contentType);
  if (r.text !== undefined) expect(result.text).toBe(r.text);
  if (r.bytesBase64 !== undefined) {
    expect(Buffer.from(result.bytes).toString('base64')).toBe(r.bytesBase64);
  }
  if ('requestId' in r) {
    expect(result.requestId ?? null).toBe(r.requestId);
  }
}

async function runRetryCase(given: any, expect_: any): Promise<void> {
  const { fetch, requests } = stubFetch(given.responses);
  const sleeps: number[] = [];
  const client = newClient(given, fetch, (ms) => sleeps.push(ms / 1000));

  const call = client.convert().fromZpl('^XA^XZ').toZpl().execute();

  if (expect_.error !== undefined) {
    const error = await call.then(
      () => { throw new Error('Expected the call to reject.'); },
      (e: unknown) => e as LabelZoomError,
    );
    assertError(expect_.error, error);
  } else {
    const result = await call;
    if (expect_.result?.text !== undefined) expect(result.text).toBe(expect_.result.text);
  }

  expect(requests.length, 'attempts').toBe(expect_.attempts);
  expect(sleeps).toEqual(expect_.sleepsSeconds);
}

async function runValidationCase(given: any, expect_: any): Promise<void> {
  const { fetch, requests } = stubFetch([OK_RESPONSE]);
  const client = newClient(given, fetch);

  let thrown: unknown;
  try {
    await buildRequest(client, given).execute();
  } catch (error) {
    thrown = error;
  }

  expect(thrown).toBeInstanceOf(LabelZoomValidationError);
  expect((thrown as LabelZoomValidationError).parameter).toBe(expect_.validationError.parameter);
  // Local validation must never reach the network.
  expect(requests.length, 'requests sent').toBe(expect_.requestsSent);
}

/**
 * The runtime stand-in for "this snippet must not compile".
 *
 * A test cannot assert a compile error about itself, so it asserts the property that makes the
 * compile error inevitable: TargetFormat contains no source-only format.
 */
function runTypecheckCase(given: any): void {
  const snippet: string = given.snippet;

  for (const sourceOnly of ['url', 'jpg']) {
    if (snippet.toLowerCase().includes(`to(${sourceOnly})`)) {
      expect(TARGET_FORMATS as readonly string[]).not.toContain(sourceOnly);
    }
  }

  if (snippet.includes('SourceFormat.')) {
    // 'pdf' is in both unions, but 'url' is the discriminator: SourceFormat is strictly wider,
    // so it cannot stand in for TargetFormat.
    expect(TARGET_FORMATS as readonly string[]).not.toContain('url');
  }
}

// -------------------------------------------------------------------- the suite

const executed = new Set<string>();

afterEach(() => {
  delete process.env.LABELZOOM_API_KEY;
});

describe('conformance', () => {
  for (const caseId of expectedCaseIds) {
    it(caseId, async () => {
      const fixture = readJson(join(ROOT, 'cases', `${caseId}.json`));
      const { given, expect: expected } = fixture;

      if (given.env !== undefined) {
        for (const [name, value] of Object.entries(given.env)) {
          process.env[name] = value as string;
        }
      }

      switch (caseId.split('/')[0]) {
        case 'request': await runRequestCase(given, expected); break;
        case 'response': await runResponseCase(given, expected); break;
        case 'retry': await runRetryCase(given, expected); break;
        case 'validation': await runValidationCase(given, expected); break;
        case 'typecheck': runTypecheckCase(given); break;
        default: throw new Error(`Unknown case kind for '${caseId}'.`);
      }

      executed.add(caseId);
    });
  }

  /**
   * The whole anti-drift mechanism. A suite that quietly runs a subset of the fixtures reports
   * success exactly like one that runs all of them, so coverage is asserted, not assumed.
   */
  it('covers every declared case', () => {
    for (const [id, reason] of Object.entries(skips)) {
      expect(allCaseIds, `skips/${LANGUAGE}.json declares unknown case '${id}'`).toContain(id);
      expect(reason.trim(), `skip '${id}' has no reason`).not.toBe('');
    }

    expect([...executed].sort()).toEqual([...expectedCaseIds].sort());
  });
});
