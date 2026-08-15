import { LabelZoomValidationError } from './errors.js';
import type { ColorMode, PdfConversionMode, ZplImageCompression } from './formats.js';

/** A single label's variable-field values. Each record produces one label. */
export type DataRecord = Record<string, unknown>;

/**
 * Conversion options, in the shape the API expects.
 *
 * Every field is optional and **only the ones you set are sent**. The SDK never fills in a
 * client-side default, so a change to a server default reaches you without an SDK upgrade.
 */
export interface ConversionOptions {
  /** Output resolution. Server default 203. */
  dpi?: number;
  /** Degrees clockwise. Must be a multiple of 90. Server default 0. */
  rotation?: number;
  /** Percent. Server default 100. */
  scaling?: number;
  /** Server default `GRAYSCALE`. */
  colorMode?: ColorMode;
  /** Luminance threshold 0–100. Server default 70. */
  darkness?: number;
  /** Pixel offset of the extracted region. */
  position?: { x: number; y: number };
  /** Forced on for the anonymous free tier regardless. */
  watermark?: boolean;
  /** e.g. `moca`. Requires a paid license. */
  dialect?: string;
  /** One entry per output label. A single record is wrapped into a one-element array. */
  data?: DataRecord | DataRecord[];
  /** **Inches**, not dots. */
  label?: { width?: number; height?: number };
  pdf?: {
    /** Server default `IMAGE`. */
    conversionMode?: PdfConversionMode;
    /** **0-based**. Omit to convert every page. */
    pageNumber?: number;
  };
  zpl?: {
    /** e.g. `['^PQ']`. */
    commandsToIgnore?: string[];
    /** Server default `Z64`. */
    imageCompression?: ZplImageCompression;
  };
  /**
   * Anything the SDK does not model yet. Unknown keys are ignored server-side, so this is a
   * safe forward-compatibility escape hatch.
   */
  [key: string]: unknown;
}

/**
 * Validates options locally and renders them as the `params` JSON value.
 *
 * Everything travels in one `?params=<JSON>` parameter, never dot-notation. The API accepts both
 * and merges them, but dot-notation cannot express every parameter — `?data=[{}]` is rejected
 * with 400 while `?params={"data":[{}]}` succeeds. One serialization path, no special cases.
 *
 * Returns `undefined` when nothing was set, in which case no query parameter is emitted at all.
 */
export function serializeOptions(options: ConversionOptions | undefined): string | undefined {
  if (options === undefined) {
    return undefined;
  }

  const params: Record<string, unknown> = {};

  for (const [key, value] of Object.entries(options)) {
    if (value === undefined) {
      continue;
    }

    switch (key) {
      case 'rotation':
        // Rejected locally: the server would 400, and this is unambiguously a caller bug.
        if (typeof value !== 'number' || value % 90 !== 0) {
          throw new LabelZoomValidationError(
            'rotation',
            `Rotation must be a multiple of 90 degrees, but was ${String(value)}.`,
          );
        }
        params[key] = value;
        break;

      case 'darkness':
        if (typeof value !== 'number' || value < 0 || value > 100) {
          throw new LabelZoomValidationError(
            'darkness',
            `Darkness must be between 0 and 100, but was ${String(value)}.`,
          );
        }
        params[key] = value;
        break;

      case 'data':
        params[key] = normalizeData(value);
        break;

      default:
        params[key] = value;
        break;
    }
  }

  return Object.keys(params).length === 0 ? undefined : JSON.stringify(params);
}

/**
 * `data` is always an array — one entry produces one label. A caller passing a single record
 * means "one label", so a bare object is wrapped rather than rejected.
 */
function normalizeData(value: unknown): DataRecord[] {
  const records = Array.isArray(value) ? value : [value];

  return records.map((record, index) => {
    if (typeof record !== 'object' || record === null || Array.isArray(record)) {
      throw new LabelZoomValidationError(
        'data',
        `data[${index}] is ${describe(record)}; every entry must be an object whose keys are ` +
          "the label's variable field names.",
      );
    }
    return record as DataRecord;
  });
}

function describe(value: unknown): string {
  if (value === null) return 'null';
  if (Array.isArray(value)) return 'an array';
  return `a ${typeof value}`;
}
