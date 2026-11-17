import { describe, expect, it } from 'vitest';
import {
  DynamicPrintBuilder,
  DynamicSourceBuilder,
  ImagePrintBuilder,
  ImageSourceBuilder,
  LabelZoomClient,
  PdfPrintBuilder,
  PdfSourceBuilder,
  PrintBuilder,
  PrinterLanguagePrintBuilder,
  PrinterLanguageSourceBuilder,
  PrintRequestBuilder,
  SourceBuilder,
  ZplPrintBuilder,
  ZplSourceBuilder,
} from '../src/index.js';

/**
 * Pins the print stage against the convert source stage it mirrors (contract section 1.2). The print
 * builders can't reuse the convert ones, because those carry the `to*` transitions, so each format has
 * a twin. These tests stop the twins drifting.
 */

const PRINT_ONLY = new Set(['withCopies', 'withIdempotencyKey', 'send']);

/** Every public method on the class and its ancestors, excluding the constructor. */
function methods(type: abstract new (...args: never[]) => unknown): Set<string> {
  const names = new Set<string>();
  for (let proto = type.prototype; proto !== Object.prototype && proto !== null; proto = Object.getPrototypeOf(proto)) {
    for (const name of Object.getOwnPropertyNames(proto)) {
      if (name !== 'constructor') names.add(name);
    }
  }
  return names;
}

const TWINS = [
  [SourceBuilder, PrintBuilder],
  [PrinterLanguageSourceBuilder, PrinterLanguagePrintBuilder],
  [ZplSourceBuilder, ZplPrintBuilder],
  [ImageSourceBuilder, ImagePrintBuilder],
  [PdfSourceBuilder, PdfPrintBuilder],
  [DynamicSourceBuilder, DynamicPrintBuilder],
] as const;

describe('print stages', () => {
  for (const [convert, print] of TWINS) {
    it(`${print.name} offers exactly the source options of ${convert.name}`, () => {
      const convertOptions = [...methods(convert)].filter((name) => !/^to[A-Z(]|^to$/.test(name)).sort();
      const printOptions = [...methods(print)].filter((name) => !PRINT_ONLY.has(name)).sort();
      expect(printOptions).toEqual(convertOptions);
    });

    it(`${print.name} has no target stage`, () => {
      expect([...methods(print)].filter((name) => name.startsWith('to'))).toEqual([]);
      expect(methods(print).has('send')).toBe(true);
    });
  }

  it('offers from* methods only for the formats print converts', () => {
    const offered = methods(PrintRequestBuilder);
    for (const name of ['fromUrl', 'fromBase64Text', 'fromEscLabel', 'fromEzpl', 'fromTpcl', 'fromNlbl', 'fromLbl', 'fromBtw', 'fromDymo']) {
      expect(offered.has(name), name).toBe(false);
    }
    for (const name of ['fromZpl', 'fromPdf', 'fromPng', 'fromFingerprint', 'fromCpcl', 'fromTemplate']) {
      expect(offered.has(name), name).toBe(true);
    }
  });

  it('sends a typed chain with its source options, copies and key', async () => {
    const requests: { url: URL; headers: Record<string, string> }[] = [];
    const client = new LabelZoomClient({
      apiKey: 'lz_test',
      maxRetries: 0,
      fetch: (async (input: string | URL, init?: RequestInit) => {
        requests.push({ url: new URL(String(input)), headers: init?.headers as Record<string, string> });
        return new Response('{"jobId":"j1","status":"queued","copies":2}', {
          status: 200,
          headers: { 'content-type': 'application/json' },
        });
      }) as typeof fetch,
    });

    const job = await client
      .print('printer-1')
      .fromPdf(new Uint8Array([0x25, 0x50, 0x44, 0x46]))
      .withPage(1)
      .withSplit({ rows: 2 })
      .withCopies(2)
      .withIdempotencyKey('order-42')
      .send();

    const { url, headers } = requests[0]!;
    expect(url.pathname).toBe('/api/v3/printers/printer-1/print');
    expect(url.searchParams.get('sourceFormat')).toBe('pdf');
    expect(url.searchParams.get('copies')).toBe('2');
    expect(JSON.parse(url.searchParams.get('params')!)).toEqual({ pdf: { pageNumber: 1 }, split: { rows: 2 } });
    expect(headers['idempotency-key']).toBe('order-42');
    expect(headers['content-type']).toBe('application/pdf');
    expect(job).toEqual({ jobId: 'j1', status: 'queued', copies: 2, idempotent: false });
  });

  it('gives each call its own generated key', async () => {
    const keys: string[] = [];
    const client = new LabelZoomClient({
      apiKey: 'lz_test',
      maxRetries: 0,
      fetch: (async (_input: string | URL, init?: RequestInit) => {
        keys.push((init?.headers as Record<string, string>)['idempotency-key']!);
        return new Response('{"jobId":"j","status":"queued"}', { status: 200 });
      }) as typeof fetch,
    });

    const print = client.print('printer-1').fromZpl('^XA^XZ');
    await print.send();
    await print.send();

    // Two calls are two prints. Only a retry within one call may reuse a key.
    expect(keys[0]).not.toBe(keys[1]);
  });
});
