/** Fields every API error carries. */
export interface LabelZoomErrorInit {
  status: number;
  message: string;
  requestId?: string;
  rawBody?: string;
}

/**
 * Base type for every error the LabelZoom API returns.
 *
 * Catch this to handle them all. Note that {@link LabelZoomValidationError} deliberately does
 * *not* extend it.
 */
export class LabelZoomError extends Error {
  /** The HTTP status code the API returned. */
  readonly status: number;

  /**
   * The `X-LZ-Request-Id` response header, if present. Quote this to LabelZoom support — it
   * identifies the exact request server-side.
   */
  readonly requestId?: string;

  /**
   * The raw response body, untruncated. `message` is derived from it and capped at 512
   * characters; this is not.
   */
  readonly rawBody?: string;

  constructor(init: LabelZoomErrorInit) {
    super(init.message);
    this.name = new.target.name;
    this.status = init.status;
    this.requestId = init.requestId;
    this.rawBody = init.rawBody;
    // Without this, `instanceof` fails for subclasses when the package is consumed as CJS
    // compiled to an ES5-ish target.
    Object.setPrototypeOf(this, new.target.prototype);
  }
}

/** HTTP 400. The request was malformed or the conversion path is invalid. */
export class BadRequestError extends LabelZoomError {}

/** HTTP 401. The supplied credential was rejected. */
export class UnauthorizedError extends LabelZoomError {}

/** HTTP 403. The credential is valid but not entitled to this operation. */
export class ForbiddenError extends LabelZoomError {
  /**
   * True when this 403 is a paywall rather than a permissions problem — "JSON export is a paid
   * feature" and friends. By far the most common anonymous-tier failure, so it gets a flag
   * instead of leaving callers to match strings.
   */
  readonly isPaidFeature: boolean;

  constructor(init: LabelZoomErrorInit & { isPaidFeature: boolean }) {
    super(init);
    this.isPaidFeature = init.isPaidFeature;
  }
}

/** HTTP 404. The conversion path does not exist. */
export class NotFoundError extends LabelZoomError {}

/** HTTP 413. The body exceeded the tier's limit — 1 MB on the anonymous free tier. */
export class PayloadTooLargeError extends LabelZoomError {}

/** HTTP 429. Too many requests. */
export class RateLimitedError extends LabelZoomError {
  /**
   * `Retry-After` in seconds, when the server sent one. The SDK already honours this during its
   * own retries; this exposes it for callers doing their own.
   */
  readonly retryAfterSeconds?: number;

  constructor(init: LabelZoomErrorInit & { retryAfterSeconds?: number }) {
    super(init);
    this.retryAfterSeconds = init.retryAfterSeconds;
  }
}

/** HTTP 5xx. Retried automatically before surfacing. */
export class ServerError extends LabelZoomError {}

/**
 * A request was rejected locally, before any network call.
 *
 * Deliberately *not* a {@link LabelZoomError}: this is a bug in the calling code, not a server
 * response. It carries no status, it is never retried, and a caller catching `LabelZoomError` to
 * implement fallback behaviour should not swallow it.
 */
export class LabelZoomValidationError extends Error {
  /** The conversion parameter at fault, named as it appears on the wire. */
  readonly parameter: string;

  constructor(parameter: string, message: string) {
    super(message);
    this.name = 'LabelZoomValidationError';
    this.parameter = parameter;
    Object.setPrototypeOf(this, LabelZoomValidationError.prototype);
  }
}

const MAX_MESSAGE_LENGTH = 512;

/**
 * Pulls the human-readable detail out of an error body.
 *
 * Both shapes in play put it on `message`: the gateway returns `{"message": "..."}` and Spring
 * returns `{timestamp, status, error, message, path}`. Anything else — a rate-limit body keyed
 * on `error`, an HTML 502, a truncated JSON fragment — falls through to the raw text, then to
 * the status text.
 */
export function extractMessage(rawBody: string | undefined, statusText: string | undefined): string {
  if (rawBody !== undefined && rawBody.trim() !== '') {
    try {
      const parsed: unknown = JSON.parse(rawBody);
      if (
        typeof parsed === 'object' &&
        parsed !== null &&
        'message' in parsed &&
        typeof (parsed as { message: unknown }).message === 'string' &&
        (parsed as { message: string }).message.trim() !== ''
      ) {
        return truncate((parsed as { message: string }).message);
      }
    } catch {
      // Not JSON, or malformed. The raw body is still the most useful thing available.
    }

    return truncate(rawBody.trim());
  }

  return statusText !== undefined && statusText.trim() !== ''
    ? statusText
    : 'The LabelZoom API returned an error with no response body.';
}

function truncate(value: string): string {
  return value.length <= MAX_MESSAGE_LENGTH ? value : value.slice(0, MAX_MESSAGE_LENGTH);
}

export function errorForStatus(init: LabelZoomErrorInit & { retryAfterSeconds?: number }): LabelZoomError {
  switch (true) {
    case init.status === 400:
      return new BadRequestError(init);
    case init.status === 401:
      return new UnauthorizedError(init);
    case init.status === 403:
      return new ForbiddenError({ ...init, isPaidFeature: /paid feature/i.test(init.message) });
    case init.status === 404:
      return new NotFoundError(init);
    case init.status === 413:
      return new PayloadTooLargeError(init);
    case init.status === 429:
      return new RateLimitedError(init);
    case init.status >= 500:
      return new ServerError(init);
    default:
      return new LabelZoomError(init);
  }
}
