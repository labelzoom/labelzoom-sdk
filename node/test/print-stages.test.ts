import { describe, expect, it } from 'vitest';
import {
  DynamicSourceBuilder,
  ImageSourceBuilder,
  LabelZoomClient,
  LabelZoomValidationError,
  PdfSourceBuilder,
  PrinterLanguageSourceBuilder,
  PrinterTargetBuilder,
  SourceBuilder,
  TemplatePrinterTargetBuilder,
  TemplateSourceBuilder,
  ZplSourceBuilder,
} from '../src/index.js';

/**
 * Pins the printer target (contract section 1 and P2): every source stage reaches a printer through
 * `toPrinter`, and the printer stages offer only the options that apply to a printer.
 */

/** The public methods a class declares itself, excluding the constructor. */
function ownMethods(type: abstract new (...args: never[]) => unknown): string[] {
  return Object.getOwnPropertyNames(type.prototype).filter((name) => name !== 'constructor').sort();
}

function jsonClient(requests: { url: URL; headers: Record<string, string>; body: unknown }[], responses: string[]) {
  return new LabelZoomClient({
    apiKey: 'lz_test',
    maxRetries: 0,
    fetch: (async (input: string | URL, init?: RequestInit) => {
      requests.push({ url: new URL(String(input)), headers: init?.headers as Record<string, string>, body: init?.body });
      return new Response(responses.shift() ?? '{"jobId":"j","status":"queued"}', {
        status: 200,
        headers: { 'content-type': 'application/json' },
      });
    }) as typeof fetch,
  });
}

describe('printer target', () => {
  for (const stage of [SourceBuilder, PrinterLanguageSourceBuilder, ZplSourceBuilder, ImageSourceBuilder, PdfSourceBuilder, DynamicSourceBuilder]) {
    it(`${stage.name} can target a printer`, () => {
      expect(typeof (stage.prototype as unknown as { toPrinter?: unknown }).toPrinter).toBe('function');
    });
  }

  it('offers only printer options on the printer stage', () => {
    expect(ownMethods(PrinterTargetBuilder)).toEqual(['execute', 'withCopies', 'withIdempotencyKey', 'withRotation']);
  });

  it('gives a template merge data and only a printer target', () => {
    expect(ownMethods(TemplateSourceBuilder)).toEqual(['toPrinter', 'withData', 'withValidate']);
    expect(ownMethods(TemplatePrinterTargetBuilder)).toEqual(['execute', 'withCopies', 'withIdempotencyKey']);
  });

  it('refuses a URL and base64 text, which print cannot read', () => {
    const client = new LabelZoomClient({ apiKey: 'lz_test' });
    for (const call of [
      () => client.convert().fromUrl('https://example.com/label.zpl').toPrinter('p1'),
      () => client.convert().fromBase64Text('pdf', 'JVBERi0=').toPrinter('p1'),
    ]) {
      expect(call).toThrow(LabelZoomValidationError);
      try { call(); } catch (error) { expect((error as LabelZoomValidationError).parameter).toBe('source'); }
    }
  });

  it('sends a typed chain with its source options, rotation, copies and key', async () => {
    const requests: { url: URL; headers: Record<string, string>; body: unknown }[] = [];
    const client = jsonClient(requests, ['{"jobId":"j1","status":"queued","copies":2}']);

    const job = await client
      .convert()
      .fromPdf(new Uint8Array([0x25, 0x50, 0x44, 0x46]))
      .withPage(1)
      .withSplit({ rows: 2 })
      .toPrinter('printer-1')
      .withRotation(90)
      .withCopies(2)
      .withIdempotencyKey('order-42')
      .execute();

    const { url, headers } = requests[0]!;
    expect(url.pathname).toBe('/api/v3/printers/printer-1/print');
    expect(url.searchParams.get('sourceFormat')).toBe('pdf');
    expect(url.searchParams.get('copies')).toBe('2');
    expect(JSON.parse(url.searchParams.get('params')!)).toEqual({ pdf: { pageNumber: 1 }, split: { rows: 2 }, rotation: 90 });
    expect(headers['idempotency-key']).toBe('order-42');
    expect(headers['content-type']).toBe('application/pdf');
    expect(job).toEqual({ jobId: 'j1', status: 'queued', copies: 2, idempotent: false });
  });

  it('prints a design file as its own bytes', async () => {
    const requests: { url: URL; headers: Record<string, string>; body: unknown }[] = [];
    const client = jsonClient(requests, []);

    await client.convert().fromBtw(new Uint8Array([0x00, 0xff, 0x10])).toPrinter('p1').execute();

    expect(requests[0]!.url.searchParams.get('sourceFormat')).toBe('btw');
    expect(requests[0]!.headers['content-type']).toBe('application/octet-stream');
    expect([...new Uint8Array(requests[0]!.body as ArrayBuffer)]).toEqual([0x00, 0xff, 0x10]);
  });

  it('gives each call its own generated key', async () => {
    const requests: { url: URL; headers: Record<string, string>; body: unknown }[] = [];
    const client = jsonClient(requests, []);

    const print = client.convert().fromZpl('^XA^XZ').toPrinter('printer-1');
    await print.execute();
    await print.execute();

    // Two calls are two prints. Only a retry within one call may reuse a key.
    expect(requests[0]!.headers['idempotency-key']).not.toBe(requests[1]!.headers['idempotency-key']);
  });
});
