import type { ConversionBody, LabelZoomClient } from './client.js';
import { LabelZoomValidationError } from './errors.js';
import type { SourceFormat } from './formats.js';
import type { ConversionOptions, DataRecord } from './options.js';

/**
 * Every source format prints except `url`: print doesn't fetch. Base64 text can't be printed either,
 * because print reads the document's own bytes.
 */
export type PrintSourceFormat = Exclude<SourceFormat, 'url'>;

/**
 * The options a print takes: the source options, which decide how the document is read, and
 * `rotation`. The printer's configuration picks the output, so no other target option applies.
 */
export type PrintOptions = Pick<
  ConversionOptions,
  'sourceDpi' | 'scaling' | 'colorMode' | 'darkness' | 'position' | 'data' | 'label' | 'pdf' | 'split' | 'labelIndex' | 'rotation'
> & { zpl?: { commandsToIgnore?: string[] } };

/** A document print in one call. The chain, `convert().fromZpl(...).toPrinter(printerId)`, builds one of these. */
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
  requirePrintable<T extends { source: SourceFormat; asBase64Text: boolean }>(draft: T): T {
    if (draft.source === 'url') {
      throw new LabelZoomValidationError('source', "A URL can't be printed: print doesn't fetch. Fetch the document and print its bytes.");
    }
    if (draft.asBase64Text) {
      throw new LabelZoomValidationError('source', "Base64 text can't be printed: print reads the document's own bytes. Decode it first.");
    }
    return draft;
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

/** The document under construction, as the convert source stage built it. */
interface SourceDraft {
  readonly client: LabelZoomClient;
  readonly source: SourceFormat;
  readonly body: ConversionBody;
  readonly asBase64Text: boolean;
  readonly options: ConversionOptions;
}

/**
 * The printer target stage, after `toPrinter(printerId)`: how many times, and the key that makes the
 * print safe to repeat. The printer sets the output language, so the only target option is
 * `withRotation`, which turns the finished label whatever the language.
 */
export class PrinterTargetBuilder {
  readonly #draft: SourceDraft;
  readonly #printerId: string;
  #copies?: number;
  #idempotencyKey?: string;

  /** @internal */
  constructor(draft: SourceDraft, printerId: string) {
    this.#draft = draft;
    this.#printerId = printerId;
  }

  /** Must be a multiple of 90; rejected locally otherwise. */
  withRotation(rotation: number): this { this.#draft.options.rotation = rotation; return this; }

  /** Sends the whole job this many times, 1 to 9999. `^PQ3` with 5 copies prints 15 labels. */
  withCopies(copies: number): this { this.#copies = printWriters.copies(copies); return this; }

  /**
   * Makes the print safe to repeat: the same key returns the original job instead of printing again. Use
   * something unique to the label, such as an order number. A deliberate reprint needs a new key.
   */
  withIdempotencyKey(key: string): this { this.#idempotencyKey = printWriters.idempotencyKey(key); return this; }

  /** Sends the document to the printer. Options are copied now, so the builder can be executed again. */
  execute(signal?: AbortSignal): Promise<PrintJob> {
    return this.#draft.client.print({
      printerId: this.#printerId,
      from: this.#draft.source as PrintSourceFormat,
      body: this.#draft.body,
      options: structuredClone(this.#draft.options) as PrintOptions,
      copies: this.#copies,
      idempotencyKey: this.#idempotencyKey,
      signal,
    });
  }
}

/**
 * The source stage for a published template, after `convert().fromTemplate(templateId)`. A template is
 * already a label, so it takes merge data instead of source options; its only target is a printer.
 */
export class TemplateSourceBuilder {
  readonly #client: LabelZoomClient;
  readonly #templateId: string;
  #data?: DataRecord[];
  #validate?: boolean;

  /** @internal */
  constructor(client: LabelZoomClient, templateId: string) {
    this.#client = client;
    this.#templateId = templateId;
  }

  /** One object of merge-field values per label. */
  withData(...records: DataRecord[]): this {
    if (records.length === 0) {
      throw new LabelZoomValidationError('data', 'Provide at least one record, or omit withData for a template with no merge fields.');
    }
    this.#data = records;
    return this;
  }

  /** Checks every record for missing merge fields before anything prints. */
  withValidate(validate = true): this { this.#validate = validate; return this; }

  /** Prints one label per record on a Cloud Print printer, converted to its language. */
  toPrinter(printerId: string): TemplatePrinterTargetBuilder {
    return new TemplatePrinterTargetBuilder(this.#client, {
      printerId: printWriters.requireId(printerId, 'printerId'),
      templateId: this.#templateId,
      data: this.#data,
      validate: this.#validate,
    });
  }
}

/** The printer target stage for a template, after `fromTemplate(...).toPrinter(printerId)`. */
export class TemplatePrinterTargetBuilder {
  readonly #client: LabelZoomClient;
  readonly #request: PrintTemplateRequest;

  /** @internal */
  constructor(client: LabelZoomClient, request: PrintTemplateRequest) {
    this.#client = client;
    this.#request = request;
  }

  /** Sends each label this many times, 1 to 9999. */
  withCopies(copies: number): this { this.#request.copies = printWriters.copies(copies); return this; }

  /** One key for the whole batch; the server derives each record's key from it. */
  withIdempotencyKey(key: string): this { this.#request.idempotencyKey = printWriters.idempotencyKey(key); return this; }

  /** Sends the labels. If `remaining` isn't 0, send again with the same key to submit the rest. */
  execute(signal?: AbortSignal): Promise<PrintBatch> {
    return this.#client.printTemplate({ ...this.#request, data: structuredClone(this.#request.data), signal });
  }
}

/** Print jobs, from `client.jobs`. */
export interface PrintJobsClient {
  /** Gets a job's current state. Poll it to follow a job to `completed` or `failed`. History is kept for 30 days. */
  get(jobId: string, signal?: AbortSignal): Promise<PrintJobDetails>;
}
