import type { ConversionBody, LabelZoomClient } from './client.js';
import { setPdf, setRaster, setSplit, setZpl, type RasterOptions, type SplitOptions } from './builders.js';
import { LabelZoomValidationError } from './errors.js';
import type { PdfConversionMode, SourceFormat } from './formats.js';
import type { ConversionOptions, DataRecord } from './options.js';

/**
 * The formats print converts to the printer's language. The print endpoint sends any other format to
 * the printer as is, so the SDK doesn't offer it: convert it to the printer's language first.
 */
export type PrintSourceFormat = Extract<
  SourceFormat,
  'zpl' | 'epl' | 'ipl' | 'tspl' | 'dpl' | 'sbpl' | 'fingerprint' | 'cpcl' | 'xml' | 'json' | 'pdf' | 'png' | 'bmp' | 'gif' | 'jpeg' | 'jpg'
>;

export const PRINT_SOURCE_FORMATS: readonly PrintSourceFormat[] = [
  'zpl', 'epl', 'ipl', 'tspl', 'dpl', 'sbpl', 'fingerprint', 'cpcl',
  'xml', 'json', 'pdf', 'png', 'bmp', 'gif', 'jpeg', 'jpg',
];

/**
 * The source options a print takes: how the document is read. There are no target options, because the
 * printer's configuration picks the output.
 */
export type PrintOptions = Pick<
  ConversionOptions,
  'sourceDpi' | 'scaling' | 'colorMode' | 'darkness' | 'position' | 'data' | 'label' | 'pdf' | 'split' | 'labelIndex'
> & { zpl?: { commandsToIgnore?: string[] } };

/** A document print in one call. The chain, `client.print(printerId).fromZpl(...)`, builds one of these. */
export interface PrintRequest {
  printerId: string;
  from: PrintSourceFormat;
  body: ConversionBody;
  options?: PrintOptions;
  /** Sends the whole job this many times, 1 to 9999. It multiplies a quantity in the document. */
  copies?: number;
  /**
   * Makes the print safe to repeat: the same key returns the original job instead of printing again.
   * Without one, the SDK generates a key per call, so its own retries can't print twice.
   */
  idempotencyKey?: string;
  signal?: AbortSignal;
}

/** A template print in one call. */
export interface PrintTemplateRequest {
  printerId: string;
  templateId: string;
  /** One object of merge-field values per label. Omit it for a template with no merge fields. */
  data?: DataRecord | DataRecord[];
  /** Checks every record for missing merge fields before anything prints. */
  validate?: boolean;
  copies?: number;
  /** One key for the whole batch; the server derives each record's key from it. */
  idempotencyKey?: string;
  signal?: AbortSignal;
}

/** A print job the server accepted. */
export interface PrintJob {
  jobId: string;
  /** `dispatched` when an online agent took it, `queued` when none is online. Statuses can be added. */
  status: string;
  /** The copies requested, or `null` when the request didn't set any. */
  copies: number | null;
  /** True when the request reused an Idempotency-Key: this is the original job and nothing printed again. */
  idempotent: boolean;
}

/** The jobs a template print created, one per record. */
export interface PrintBatch {
  jobs: PrintJob[];
  /**
   * Records at the end of the batch that weren't submitted. Send the same request with the same
   * Idempotency-Key to submit them; records already submitted replay without printing again.
   */
  remaining: number;
  /** The server's explanation when `remaining` isn't 0, else `null`. */
  message: string | null;
}

/** A print job's current state. */
export interface PrintJobDetails {
  id: string;
  printerId: string | null;
  agentId: string | null;
  /** The format the job was stored in: the printer's language after any conversion. */
  sourceFormat: string | null;
  copies: number | null;
  /** `queued`, `dispatched`, `printing`, `completed` or `failed`. */
  status: string;
  /** Why the job failed, or `null`. */
  error: string | null;
  retryCount: number;
  createdDate: Date | null;
  dispatchedDate: Date | null;
  completedDate: Date | null;
  retriedDate: Date | null;
}

/** One counted quota. `pending` is for agents only, `resetAt` for jobs today only. */
export interface PrintQuotaCounter {
  used: number;
  limit: number;
  remaining: number;
  pending?: number;
  resetAt?: Date | null;
}

/** The account's Cloud Print limits and today's usage. */
export interface PrintQuota {
  plan: string;
  agents: PrintQuotaCounter;
  printers: PrintQuotaCounter;
  /** Resets at `jobsToday.resetAt`, 00:00 UTC. */
  jobsToday: PrintQuotaCounter;
  limits: { maxPayloadBytes: number; maxStoredPayloadBytes: number; maxBatchSize: number };
}

const MIN_COPIES = 1;
const MAX_COPIES = 9999;

/** @internal The print options' validation, written once for both print calls. */
export const printWriters = {
  requireId(id: unknown, parameter: string): string {
    if (typeof id !== 'string' || id.trim() === '') {
      throw new LabelZoomValidationError(parameter, `${parameter} cannot be empty.`);
    }
    return id;
  },
  requirePrintable(format: SourceFormat): PrintSourceFormat {
    if (!(PRINT_SOURCE_FORMATS as readonly string[]).includes(format)) {
      throw new LabelZoomValidationError(
        'source',
        `${String(format)} can't be printed directly: the print endpoint converts only ZPL, EPL, IPL, TSPL, DPL, ` +
          'SBPL, Fingerprint, CPCL, LabelZoom XML and JSON, PDF and images. Convert it to the printer\'s language ' +
          'first, then print the result.',
      );
    }
    return format as PrintSourceFormat;
  },
  copies(copies: number): number {
    if (!Number.isInteger(copies) || copies < MIN_COPIES || copies > MAX_COPIES) {
      throw new LabelZoomValidationError('copies', `copies must be a whole number from 1 to 9999, but was ${copies}.`);
    }
    return copies;
  },
  idempotencyKey(key: string): string {
    if (typeof key !== 'string' || key.trim() === '') {
      throw new LabelZoomValidationError(
        'idempotencyKey',
        'An Idempotency-Key cannot be blank. Omit it to have the SDK generate one.',
      );
    }
    return key;
  },
  /** The key for one call, fixed before the first attempt so every retry sends it (contract P4). */
  keyForCall(callerKey: string | undefined): string {
    return callerKey ?? globalThis.crypto.randomUUID();
  },
};

// ------------------------------------------------------------------ responses

type Json = Record<string, unknown>;

const str = (json: Json, name: string): string | null => (typeof json[name] === 'string' ? (json[name] as string) : null);
const int = (json: Json, name: string): number | null => (typeof json[name] === 'number' ? (json[name] as number) : null);
const instant = (json: Json, name: string): Date | null => {
  const text = str(json, name);
  if (text === null) return null;
  const date = new Date(text);
  return Number.isNaN(date.getTime()) ? null : date;
};
const obj = (json: Json, name: string): Json =>
  typeof json[name] === 'object' && json[name] !== null ? (json[name] as Json) : {};

/** @internal */
export function readPrintJob(json: Json): PrintJob {
  return {
    jobId: str(json, 'jobId') ?? '',
    status: str(json, 'status') ?? '',
    copies: int(json, 'copies'),
    idempotent: json.idempotent === true,
  };
}

/** @internal One record comes back as a bare job, several as `{ jobs: [...] }`. Callers get one shape. */
export function readPrintBatch(json: Json): PrintBatch {
  if (!Array.isArray(json.jobs)) {
    return { jobs: [readPrintJob(json)], remaining: 0, message: null };
  }
  return {
    jobs: (json.jobs as Json[]).map(readPrintJob),
    remaining: int(json, 'remaining') ?? 0,
    message: str(json, 'message'),
  };
}

/** @internal The wire is snake_case. */
export function readPrintJobDetails(json: Json): PrintJobDetails {
  return {
    id: str(json, 'id') ?? '',
    printerId: str(json, 'printer_id'),
    agentId: str(json, 'agent_id'),
    sourceFormat: str(json, 'source_format'),
    copies: int(json, 'copies'),
    status: str(json, 'status') ?? '',
    error: str(json, 'error'),
    retryCount: int(json, 'retry_count') ?? 0,
    createdDate: instant(json, 'created_date'),
    dispatchedDate: instant(json, 'dispatched_date'),
    completedDate: instant(json, 'completed_date'),
    retriedDate: instant(json, 'retried_date'),
  };
}

/** @internal */
export function readPrintQuota(json: Json): PrintQuota {
  const counter = (group: Json): PrintQuotaCounter => {
    const result: PrintQuotaCounter = {
      used: int(group, 'used') ?? 0,
      limit: int(group, 'limit') ?? 0,
      remaining: int(group, 'remaining') ?? 0,
    };
    if (int(group, 'pending') !== null) result.pending = int(group, 'pending')!;
    if ('resetAt' in group) result.resetAt = instant(group, 'resetAt');
    return result;
  };
  const limits = obj(json, 'limits');
  return {
    plan: str(json, 'plan') ?? '',
    agents: counter(obj(json, 'agents')),
    printers: counter(obj(json, 'printers')),
    jobsToday: counter(obj(json, 'jobsToday')),
    limits: {
      maxPayloadBytes: int(limits, 'maxPayloadBytes') ?? 0,
      maxStoredPayloadBytes: int(limits, 'maxStoredPayloadBytes') ?? 0,
      maxBatchSize: int(limits, 'maxBatchSize') ?? 0,
    },
  };
}

// ------------------------------------------------------------------ the chain

interface PrintDraft {
  readonly client: LabelZoomClient;
  readonly printerId: string;
  readonly source: PrintSourceFormat;
  readonly body: ConversionBody;
  readonly options: ConversionOptions;
  copies?: number;
  idempotencyKey?: string;
}

/**
 * Chooses the document, or a published template, for one printer. Returned by `client.print(printerId)`.
 *
 * The printer's configuration picks the output language, and LabelZoom converts the document to it, so a
 * PDF prints on a ZPL printer. There are `from*` methods only for the formats print converts.
 */
export class PrintRequestBuilder {
  readonly #client: LabelZoomClient;
  readonly #printerId: string;

  /** @internal */
  constructor(client: LabelZoomClient, printerId: string) {
    this.#client = client;
    this.#printerId = printerId;
  }

  from(format: PrintSourceFormat, body: ConversionBody): DynamicPrintBuilder {
    return new DynamicPrintBuilder(this.#draft(printWriters.requirePrintable(format), body));
  }

  fromZpl(zpl: ConversionBody): ZplPrintBuilder { return new ZplPrintBuilder(this.#draft('zpl', zpl)); }
  fromEpl(epl: ConversionBody): PrinterLanguagePrintBuilder { return new PrinterLanguagePrintBuilder(this.#draft('epl', epl)); }
  fromIpl(ipl: ConversionBody): PrinterLanguagePrintBuilder { return new PrinterLanguagePrintBuilder(this.#draft('ipl', ipl)); }
  fromTspl(tspl: ConversionBody): PrinterLanguagePrintBuilder { return new PrinterLanguagePrintBuilder(this.#draft('tspl', tspl)); }
  fromDpl(dpl: ConversionBody): PrinterLanguagePrintBuilder { return new PrinterLanguagePrintBuilder(this.#draft('dpl', dpl)); }
  fromSbpl(sbpl: ConversionBody): PrinterLanguagePrintBuilder { return new PrinterLanguagePrintBuilder(this.#draft('sbpl', sbpl)); }
  /** Honeywell/Intermec Fingerprint and Direct Protocol. */
  fromFingerprint(fingerprint: ConversionBody): PrinterLanguagePrintBuilder { return new PrinterLanguagePrintBuilder(this.#draft('fingerprint', fingerprint)); }
  /** Zebra CPCL. */
  fromCpcl(cpcl: ConversionBody): PrinterLanguagePrintBuilder { return new PrinterLanguagePrintBuilder(this.#draft('cpcl', cpcl)); }
  fromXml(xml: ConversionBody): PrintBuilder { return new PrintBuilder(this.#draft('xml', xml)); }
  fromJson(json: ConversionBody): PrintBuilder { return new PrintBuilder(this.#draft('json', json)); }
  fromPdf(pdf: ConversionBody): PdfPrintBuilder { return new PdfPrintBuilder(this.#draft('pdf', pdf)); }
  fromPng(png: ConversionBody): ImagePrintBuilder { return new ImagePrintBuilder(this.#draft('png', png)); }
  fromBmp(bmp: ConversionBody): ImagePrintBuilder { return new ImagePrintBuilder(this.#draft('bmp', bmp)); }
  fromGif(gif: ConversionBody): ImagePrintBuilder { return new ImagePrintBuilder(this.#draft('gif', gif)); }
  fromJpeg(jpeg: ConversionBody): ImagePrintBuilder { return new ImagePrintBuilder(this.#draft('jpeg', jpeg)); }

  /** A template published from LabelZoom Studio or the web designer. Supply its values with `withData`. */
  fromTemplate(templateId: string): TemplatePrintBuilder {
    return new TemplatePrintBuilder(this.#client, this.#printerId, printWriters.requireId(templateId, 'templateId'));
  }

  #draft(source: PrintSourceFormat, body: ConversionBody): PrintDraft {
    return { client: this.#client, printerId: this.#printerId, source, body, options: {} };
  }
}

/**
 * The print source stage: how the document is read, then the print options, then `send`.
 *
 * It mirrors `SourceStage` without the `to*` transitions, because the printer picks the output. Each print
 * builder offers exactly the source options its convert builder does, through the same writers, and a
 * stage-shape test holds the two sets equal.
 */
export abstract class PrintStage {
  protected readonly draft: PrintDraft;

  protected constructor(draft: PrintDraft) {
    this.draft = draft;
  }

  /** **Inches**, not dots. Replaces the size the document declares; content past it is clipped. */
  withLabelSize(widthInches: number, heightInches: number): this {
    this.draft.options.label = { width: widthInches, height: heightInches };
    return this;
  }

  /** **0-based** index of the one label to print. */
  withLabelIndex(zeroBasedIndex: number): this {
    this.draft.options.labelIndex = zeroBasedIndex;
    return this;
  }

  /** One label per record. */
  withData(...records: DataRecord[]): this {
    this.draft.options.data = records;
    return this;
  }

  /** Sends the whole job this many times, 1 to 9999. `^PQ3` with 5 copies prints 15 labels. */
  withCopies(copies: number): this {
    this.draft.copies = printWriters.copies(copies);
    return this;
  }

  /**
   * Makes the print safe to repeat: the same key returns the original job instead of printing again. Use
   * something unique to the label, such as an order number. A deliberate reprint needs a new key.
   */
  withIdempotencyKey(key: string): this {
    this.draft.idempotencyKey = printWriters.idempotencyKey(key);
    return this;
  }

  /** Sends the document to the printer. Options are copied now, so the builder can be sent again. */
  send(signal?: AbortSignal): Promise<PrintJob> {
    return this.draft.client.print({
      printerId: this.draft.printerId,
      from: this.draft.source,
      body: this.draft.body,
      options: structuredClone(this.draft.options) as PrintOptions,
      copies: this.draft.copies,
      idempotencyKey: this.draft.idempotencyKey,
      signal,
    });
  }
}

/** Prints LabelZoom XML or JSON. They record their own resolution, so there is no `withDpi`. */
export class PrintBuilder extends PrintStage {
  /** @internal */
  constructor(draft: PrintDraft) { super(draft); }
}

/** Prints EPL, IPL, TSPL, DPL, SBPL, Fingerprint or CPCL. */
export class PrinterLanguagePrintBuilder extends PrintStage {
  /** @internal */
  constructor(draft: PrintDraft) { super(draft); }

  /** The resolution the document was made for. Server default 203. */
  withDpi(dpi: number): this { this.draft.options.sourceDpi = dpi; return this; }
}

/** Prints a ZPL document. */
export class ZplPrintBuilder extends PrintStage {
  /** @internal */
  constructor(draft: PrintDraft) { super(draft); }

  /** The resolution the document was made for. Server default 203. */
  withDpi(dpi: number): this { this.draft.options.sourceDpi = dpi; return this; }

  /** ZPL commands to skip while reading, such as `^PQ`. */
  withCommandsToIgnore(...commands: string[]): this {
    setZpl(this.draft, { commandsToIgnore: commands });
    return this;
  }
}

/** Prints a PNG, BMP, GIF or JPEG image. */
export class ImagePrintBuilder extends PrintStage {
  /** @internal */
  constructor(draft: PrintDraft) { super(draft); }

  /** The image's resolution: how its pixels map to inches. */
  withDpi(dpi: number): this { this.draft.options.sourceDpi = dpi; return this; }
  /** Cuts each image into a grid of labels. Every length is in **inches**. */
  withSplit(split: SplitOptions): this { setSplit(this.draft, split); return this; }
  /** How the image is scaled, placed and reduced to black and white. */
  withRaster(raster: RasterOptions): this { setRaster(this.draft, raster); return this; }
}

/** Prints a PDF document. */
export class PdfPrintBuilder extends PrintStage {
  /** @internal */
  constructor(draft: PrintDraft) { super(draft); }

  /** **0-based** page to print. */
  withPage(zeroBasedPageNumber: number): this { setPdf(this.draft, { pageNumber: zeroBasedPageNumber }); return this; }
  withConversionMode(mode: PdfConversionMode): this { setPdf(this.draft, { conversionMode: mode }); return this; }
  /** Cuts each page into a grid of labels. Every length is in **inches**. */
  withSplit(split: SplitOptions): this { setSplit(this.draft, split); return this; }
  /** How each page is scaled, placed and reduced to black and white. */
  withRaster(raster: RasterOptions): this { setRaster(this.draft, raster); return this; }
}

/** Prints a document whose format is chosen at run time. Every source option is available. */
export class DynamicPrintBuilder extends PrintStage {
  /** @internal */
  constructor(draft: PrintDraft) { super(draft); }

  withDpi(dpi: number): this { this.draft.options.sourceDpi = dpi; return this; }
  withPdfPage(zeroBasedPageNumber: number): this { setPdf(this.draft, { pageNumber: zeroBasedPageNumber }); return this; }
  withPdfConversionMode(mode: PdfConversionMode): this { setPdf(this.draft, { conversionMode: mode }); return this; }
  withZplCommandsToIgnore(...commands: string[]): this { setZpl(this.draft, { commandsToIgnore: commands }); return this; }
  withSplit(split: SplitOptions): this { setSplit(this.draft, split); return this; }
  withRaster(raster: RasterOptions): this { setRaster(this.draft, raster); return this; }
}

/** Prints a published template, one label per record. A template is already a label, so there are no source options. */
export class TemplatePrintBuilder {
  readonly #client: LabelZoomClient;
  readonly #request: PrintTemplateRequest;

  /** @internal */
  constructor(client: LabelZoomClient, printerId: string, templateId: string) {
    this.#client = client;
    this.#request = { printerId, templateId };
  }

  /** One object of merge-field values per label. */
  withData(...records: DataRecord[]): this {
    if (records.length === 0) {
      throw new LabelZoomValidationError('data', 'Provide at least one record, or omit withData for a template with no merge fields.');
    }
    this.#request.data = records;
    return this;
  }

  /** Checks every record for missing merge fields before anything prints. */
  withValidate(validate = true): this { this.#request.validate = validate; return this; }

  /** Sends each label this many times, 1 to 9999. */
  withCopies(copies: number): this { this.#request.copies = printWriters.copies(copies); return this; }

  /** One key for the whole batch; the server derives each record's key from it. */
  withIdempotencyKey(key: string): this { this.#request.idempotencyKey = printWriters.idempotencyKey(key); return this; }

  /** Sends the labels. If `remaining` isn't 0, send again with the same key to submit the rest. */
  send(signal?: AbortSignal): Promise<PrintBatch> {
    return this.#client.printTemplate({ ...this.#request, data: structuredClone(this.#request.data), signal });
  }
}

/** Print jobs, from `client.jobs`. */
export interface PrintJobsClient {
  /** Gets a job's current state. Poll it to follow a job to `completed` or `failed`. History is kept for 30 days. */
  get(jobId: string, signal?: AbortSignal): Promise<PrintJobDetails>;
}
