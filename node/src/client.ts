import {
  LabelZoomValidationError,
  errorForStatus,
  extractMessage,
  type LabelZoomError,
} from './errors.js';
import {
  sourceMediaType,
  sourceWireToken,
  targetWireToken,
  type SourceFormat,
  type TargetFormat,
} from './formats.js';
import { serializeOptions, type ConversionOptions } from './options.js';

/** The production API host. */
export const DEFAULT_BASE_URL = 'https://api.labelzoom.com';

/** The environment variable consulted when no credential is passed. */
export const API_KEY_ENV_VAR = 'LABELZOOM_API_KEY';

// Replaced at build time by tsup's `define` from package.json. Vitest runs the
// TypeScript source rather than the bundle, so the constant is absent there; the
// conformance fixture asserts the User-Agent's shape, not its version.
declare const __LABELZOOM_SDK_VERSION__: string | undefined;
const SDK_VERSION =
  typeof __LABELZOOM_SDK_VERSION__ === 'string' ? __LABELZOOM_SDK_VERSION__ : '0.0.0-dev';
const REQUEST_ID_HEADER = 'x-lz-request-id';

/** A document body: raw bytes, or text for the textual formats. */
export type ConversionBody = Uint8Array | ArrayBuffer | string;

export interface LabelZoomClientOptions {
  /**
   * An `lz_live_`/`lz_test_` key or a JWT.
   *
   * Leave undefined to read `LABELZOOM_API_KEY` from the environment. Pass an empty string to
   * force anonymous mode and suppress that fallback.
   *
   * Authentication is *optional*: without a credential the API serves a free tier (watermarked,
   * first label only, 1 MB cap, no multi-page/JSON-target/image-to-image).
   */
  apiKey?: string;
  /** Defaults to {@link DEFAULT_BASE_URL}. A path prefix is preserved for reverse proxies. */
  baseUrl?: string;
  /** Retries after the initial attempt. Defaults to 2 (3 attempts total). 0 disables. */
  maxRetries?: number;
  /** Per-request timeout in milliseconds. Unset means no client-side timeout. */
  timeoutMs?: number;
  /** Appended to the SDK's own User-Agent. */
  userAgentSuffix?: string;
  /** Injectable `fetch`, primarily for tests. */
  fetch?: typeof globalThis.fetch;
  /** Full jitter on retry backoff. Defaults to true; turn off for deterministic tests. */
  useJitter?: boolean;
  /** Replaces the delay between retries. Substitute a recording no-op in tests. */
  sleep?: (milliseconds: number) => Promise<void>;
}

/**
 * The outcome of a successful conversion.
 *
 * `bytes` is authoritative — PDF, PNG, BMP, GIF and JPEG targets are binary, so returning only a
 * string would silently corrupt five of the eight targets.
 */
export interface ConversionResult {
  bytes: Uint8Array;
  /** `bytes` decoded with the response charset, defaulting to UTF-8. */
  text: string;
  contentType?: string;
  status: number;
  /** `X-LZ-Request-Id`, when the server sent it. The support handle. */
  requestId?: string;
}

/** Arguments for the one-shot {@link LabelZoomClient.convert} form. */
export interface ConvertRequest {
  from: SourceFormat;
  to: TargetFormat;
  body: ConversionBody;
  options?: ConversionOptions;
  /** Send the body as base64 `text/plain` rather than the source's own media type. */
  asBase64Text?: boolean;
  signal?: AbortSignal;
}

export class LabelZoomClient {
  readonly #baseUrl: string;
  readonly #credential: string | undefined;
  readonly #maxRetries: number;
  readonly #timeoutMs: number | undefined;
  readonly #userAgent: string;
  readonly #fetch: typeof globalThis.fetch;
  readonly #useJitter: boolean;
  readonly #sleep: (milliseconds: number) => Promise<void>;

  constructor(options: LabelZoomClientOptions = {}) {
    this.#baseUrl = (options.baseUrl ?? DEFAULT_BASE_URL).replace(/\/+$/, '');
    this.#credential = resolveCredential(options.apiKey);
    this.#maxRetries = options.maxRetries ?? 2;
    this.#timeoutMs = options.timeoutMs;
    this.#fetch = options.fetch ?? globalThis.fetch;
    this.#useJitter = options.useJitter ?? true;
    this.#sleep =
      options.sleep ?? ((ms) => new Promise<void>((resolve) => setTimeout(resolve, ms)));

    if (this.#maxRetries < 0) {
      throw new RangeError('maxRetries cannot be negative.');
    }

    // The server parses a "LabelZoomStudio/" User-Agent prefix as a Studio version and silently
    // changes PDF handling for versions <= 1.8.2, so the SDK's own token must come first.
    const suffix = options.userAgentSuffix?.trim();
    this.#userAgent =
      `labelzoom-node-sdk/${SDK_VERSION} (node)` + (suffix !== undefined && suffix !== '' ? ` ${suffix}` : '');
  }

  /** Whether a credential was resolved. False means anonymous free-tier requests. */
  get isAuthenticated(): boolean {
    return this.#credential !== undefined;
  }

  /**
   * Converts in one call.
   *
   * ```ts
   * const result = await client.convert({
   *   from: 'zpl', to: 'png', body: zpl, options: { dpi: 300 },
   * });
   * ```
   */
  convert(request: ConvertRequest): Promise<ConversionResult>;
  /**
   * Starts a fluent chain.
   *
   * ```ts
   * const result = await client.convert().fromZpl(zpl).toPng().withDpi(300).execute();
   * ```
   */
  convert(): ConversionRequestBuilder;
  convert(request?: ConvertRequest): Promise<ConversionResult> | ConversionRequestBuilder {
    if (request === undefined) {
      return new ConversionRequestBuilder(this);
    }
    return this.#execute(request);
  }

  async #execute(request: ConvertRequest): Promise<ConversionResult> {
    const body = toBytes(request.body);
    if (body.byteLength === 0) {
      // The gateway rejects a zero-length body with 400; catching it here saves a round trip.
      throw new LabelZoomValidationError(
        'body',
        'Source body cannot be empty; the API rejects zero-length requests.',
      );
    }

    const params = serializeOptions(request.options);
    const url =
      `${this.#baseUrl}/api/v2/convert/${sourceWireToken(request.from)}/to/${targetWireToken(request.to)}` +
      (params === undefined ? '' : `?params=${encodeURIComponent(params)}`);

    const headers: Record<string, string> = {
      'content-type': request.asBase64Text === true ? 'text/plain' : sourceMediaType(request.from),
      // Accept must be */*. The server's `produces` list omits image/gif, image/bmp and
      // image/jpeg, so naming the target's exact media type yields a 406 from content
      // negotiation before the handler ever runs.
      accept: '*/*',
      'user-agent': this.#userAgent,
    };
    if (this.#credential !== undefined) {
      headers.authorization = `Bearer ${this.#credential}`;
    }

    const attempts = this.#maxRetries + 1;

    for (let attempt = 1; ; attempt++) {
      let response: Response;
      try {
        response = await this.#fetch(url, {
          method: 'POST',
          headers,
          // Copy into a fresh buffer so a pooled Buffer's offset cannot leak extra bytes.
          body: body.slice().buffer as ArrayBuffer,
          signal: this.#signalFor(request.signal),
        });
      } catch (cause) {
        // A caller-initiated abort is not a transport failure and must not be retried.
        if (request.signal?.aborted === true || attempt >= attempts) {
          throw cause;
        }
        await this.#delay(attempt, undefined);
        continue;
      }

      if (response.ok) {
        return await readResult(response);
      }

      const error = await readError(response);

      if (attempt >= attempts || !isRetryable(response.status)) {
        throw error;
      }

      await this.#delay(attempt, retryAfterSeconds(response));
    }
  }

  #signalFor(callerSignal: AbortSignal | undefined): AbortSignal | undefined {
    if (this.#timeoutMs === undefined) {
      return callerSignal;
    }
    const timeout = AbortSignal.timeout(this.#timeoutMs);
    return callerSignal === undefined ? timeout : AbortSignal.any([callerSignal, timeout]);
  }

  /** 1s, 2s, 4s with full jitter, overridden by a longer `Retry-After`. */
  async #delay(attempt: number, retryAfter: number | undefined): Promise<void> {
    const backoffMs = 2 ** (attempt - 1) * 1000;
    let delayMs = this.#useJitter ? backoffMs * Math.random() : backoffMs;

    // The server knows better than the backoff curve when it tells us how long to wait.
    if (retryAfter !== undefined && retryAfter * 1000 > delayMs) {
      delayMs = retryAfter * 1000;
    }

    await this.#sleep(delayMs);
  }
}

function resolveCredential(apiKey: string | undefined): string | undefined {
  if (apiKey === undefined) {
    const fromEnvironment = globalThis.process?.env?.[API_KEY_ENV_VAR];
    return fromEnvironment !== undefined && fromEnvironment !== '' ? fromEnvironment : undefined;
  }
  // An explicit empty string forces anonymous and must not fall back to the environment.
  return apiKey === '' ? undefined : apiKey;
}

function toBytes(body: ConversionBody): Uint8Array {
  if (typeof body === 'string') {
    return new TextEncoder().encode(body);
  }
  return body instanceof Uint8Array ? body : new Uint8Array(body);
}

async function readResult(response: Response): Promise<ConversionResult> {
  const bytes = new Uint8Array(await response.arrayBuffer());
  const contentType = response.headers.get('content-type') ?? undefined;

  return {
    bytes,
    text: decode(bytes, contentType),
    contentType,
    status: response.status,
    // Headers lookup is case-insensitive by spec, which matters here: the gateway sets
    // X-LZ-Request-Id but CORS exposes it as X-LZ-Request-ID.
    requestId: response.headers.get(REQUEST_ID_HEADER) ?? undefined,
  };
}

function decode(bytes: Uint8Array, contentType: string | undefined): string {
  const charset = /charset=([^;]+)/i.exec(contentType ?? '')?.[1]?.trim().replace(/^"|"$/g, '');
  try {
    return new TextDecoder(charset ?? 'utf-8').decode(bytes);
  } catch {
    // An unrecognized charset is not worth failing an otherwise good conversion.
    return new TextDecoder('utf-8').decode(bytes);
  }
}

async function readError(response: Response): Promise<LabelZoomError> {
  let rawBody = '';
  try {
    rawBody = await response.text();
  } catch {
    // A body that cannot be read must not mask the HTTP error.
  }

  return errorForStatus({
    status: response.status,
    message: extractMessage(rawBody, response.statusText),
    requestId: response.headers.get(REQUEST_ID_HEADER) ?? undefined,
    rawBody,
    retryAfterSeconds: retryAfterSeconds(response),
  });
}

function retryAfterSeconds(response: Response): number | undefined {
  const header = response.headers.get('retry-after');
  if (header === null) {
    return undefined;
  }

  const seconds = Number(header);
  if (Number.isFinite(seconds)) {
    return seconds;
  }

  const date = Date.parse(header);
  if (Number.isNaN(date)) {
    return undefined;
  }
  return Math.max(0, Math.ceil((date - Date.now()) / 1000));
}

function isRetryable(status: number): boolean {
  return status === 429 || status >= 500;
}

/**
 * Chooses the source document.
 *
 * The named `from*` methods are one-line delegations to {@link from}: discoverability without a
 * class per format, and holding no logic they cannot drift from the format table.
 */
export class ConversionRequestBuilder {
  readonly #client: LabelZoomClient;

  constructor(client: LabelZoomClient) {
    this.#client = client;
  }

  from(format: SourceFormat, body: ConversionBody): ConversionSourceBuilder {
    return new ConversionSourceBuilder(this.#client, format, body, false);
  }

  /** Uses a base64-encoded document, sent as `text/plain`. */
  fromBase64Text(format: SourceFormat, base64: string): ConversionSourceBuilder {
    return new ConversionSourceBuilder(this.#client, format, base64, true);
  }

  fromZpl(zpl: ConversionBody): ConversionSourceBuilder { return this.from('zpl', zpl); }
  fromEpl(epl: ConversionBody): ConversionSourceBuilder { return this.from('epl', epl); }
  fromTspl(tspl: ConversionBody): ConversionSourceBuilder { return this.from('tspl', tspl); }
  fromDpl(dpl: ConversionBody): ConversionSourceBuilder { return this.from('dpl', dpl); }
  fromXml(xml: ConversionBody): ConversionSourceBuilder { return this.from('xml', xml); }
  fromJson(json: ConversionBody): ConversionSourceBuilder { return this.from('json', json); }
  fromPdf(pdf: ConversionBody): ConversionSourceBuilder { return this.from('pdf', pdf); }
  fromPng(png: ConversionBody): ConversionSourceBuilder { return this.from('png', png); }
  fromBmp(bmp: ConversionBody): ConversionSourceBuilder { return this.from('bmp', bmp); }
  fromGif(gif: ConversionBody): ConversionSourceBuilder { return this.from('gif', gif); }
  fromJpeg(jpeg: ConversionBody): ConversionSourceBuilder { return this.from('jpeg', jpeg); }

  /**
   * Has the *server* fetch a URL and convert what it finds. Validate the URL first if it came
   * from untrusted input.
   */
  fromUrl(url: string): ConversionSourceBuilder { return this.from('url', url); }
}

/**
 * Chooses the target format. One class covers all eight.
 *
 * There is no `toEpl`, `toTspl` or `toDpl`, and there never will be — those formats are
 * source-only, and `TargetFormat` has no member for them.
 */
export class ConversionSourceBuilder {
  readonly #client: LabelZoomClient;
  readonly #source: SourceFormat;
  readonly #body: ConversionBody;
  readonly #asBase64Text: boolean;

  constructor(
    client: LabelZoomClient,
    source: SourceFormat,
    body: ConversionBody,
    asBase64Text: boolean,
  ) {
    this.#client = client;
    this.#source = source;
    this.#body = body;
    this.#asBase64Text = asBase64Text;
  }

  to(target: TargetFormat): ConversionTargetBuilder {
    return new ConversionTargetBuilder(
      this.#client, this.#source, target, this.#body, this.#asBase64Text);
  }

  toZpl(): ConversionTargetBuilder { return this.to('zpl'); }
  toXml(): ConversionTargetBuilder { return this.to('xml'); }
  toJson(): ConversionTargetBuilder { return this.to('json'); }
  toPdf(): ConversionTargetBuilder { return this.to('pdf'); }
  toPng(): ConversionTargetBuilder { return this.to('png'); }
  toBmp(): ConversionTargetBuilder { return this.to('bmp'); }
  toGif(): ConversionTargetBuilder { return this.to('gif'); }
  toJpeg(): ConversionTargetBuilder { return this.to('jpeg'); }
}

/** Configures and executes a conversion. */
export class ConversionTargetBuilder {
  readonly #client: LabelZoomClient;
  readonly #source: SourceFormat;
  readonly #target: TargetFormat;
  readonly #body: ConversionBody;
  readonly #asBase64Text: boolean;
  readonly #options: ConversionOptions = {};

  constructor(
    client: LabelZoomClient,
    source: SourceFormat,
    target: TargetFormat,
    body: ConversionBody,
    asBase64Text: boolean,
  ) {
    this.#client = client;
    this.#source = source;
    this.#target = target;
    this.#body = body;
    this.#asBase64Text = asBase64Text;
  }

  withDpi(dpi: number): this { this.#options.dpi = dpi; return this; }
  /** Must be a multiple of 90; rejected locally otherwise. */
  withRotation(rotation: number): this { this.#options.rotation = rotation; return this; }
  withScaling(percent: number): this { this.#options.scaling = percent; return this; }
  withColorMode(mode: ConversionOptions['colorMode']): this { this.#options.colorMode = mode; return this; }
  withDarkness(darkness: number): this { this.#options.darkness = darkness; return this; }
  withPosition(x: number, y: number): this { this.#options.position = { x, y }; return this; }
  withWatermark(watermark = true): this { this.#options.watermark = watermark; return this; }
  withDialect(dialect: string): this { this.#options.dialect = dialect; return this; }

  /** **Inches**, not dots. */
  withLabelSize(widthInches: number, heightInches: number): this {
    this.#options.label = { width: widthInches, height: heightInches };
    return this;
  }

  withPdfConversionMode(mode: NonNullable<ConversionOptions['pdf']>['conversionMode']): this {
    this.#options.pdf = { ...this.#options.pdf, conversionMode: mode };
    return this;
  }

  /** **0-based**. Omit to convert every page. */
  withPdfPage(zeroBasedPageNumber: number): this {
    this.#options.pdf = { ...this.#options.pdf, pageNumber: zeroBasedPageNumber };
    return this;
  }

  withZplCommandsToIgnore(...commands: string[]): this {
    this.#options.zpl = { ...this.#options.zpl, commandsToIgnore: commands };
    return this;
  }

  withZplImageCompression(compression: NonNullable<ConversionOptions['zpl']>['imageCompression']): this {
    this.#options.zpl = { ...this.#options.zpl, imageCompression: compression };
    return this;
  }

  /** One label per record. A single record means a single label. */
  withData(...records: Record<string, unknown>[]): this {
    this.#options.data = records;
    return this;
  }

  /** Escape hatch for anything not modeled yet. Unknown keys are ignored server-side. */
  withParameter(key: string, value: unknown): this {
    this.#options[key] = value;
    return this;
  }

  execute(signal?: AbortSignal): Promise<ConversionResult> {
    return this.#client.convert({
      from: this.#source,
      to: this.#target,
      body: this.#body,
      options: this.#options,
      asBase64Text: this.#asBase64Text,
      signal,
    });
  }
}
