/**
 * Pins which options each stage of the fluent chain offers.
 *
 * The staged builders exist so that an option can only be set where it acts. Each
 * `@ts-expect-error` below is the compile-time half: `tsc --noEmit` (the `typecheck` script)
 * fails if the line ever starts compiling. The runtime half confirms the method really is absent
 * rather than hidden by a type.
 *
 * The conformance suite runs through the dynamic builders, because its fixtures pick formats at
 * run time. The last test sends a request through the typed builders, so that path is checked on
 * the wire too.
 */
import { describe, expect, it } from 'vitest';

import {
  DynamicSourceBuilder,
  ImageSourceBuilder,
  LabelZoomClient,
  PdfSourceBuilder,
  SourceBuilder,
  ZplSourceBuilder,
} from '../src/index.js';

const client = new LabelZoomClient({ apiKey: '' });

describe('stage shape', () => {
  it('returns a source stage typed for the format', () => {
    expect(client.convert().fromZpl('^XA^XZ')).toBeInstanceOf(ZplSourceBuilder);
    expect(client.convert().fromPdf(new Uint8Array([1]))).toBeInstanceOf(PdfSourceBuilder);
    expect(client.convert().fromPng(new Uint8Array([1]))).toBeInstanceOf(ImageSourceBuilder);
    expect(client.convert().fromEpl('N')).toBeInstanceOf(SourceBuilder);
    expect(client.convert().from('zpl', '^XA^XZ')).toBeInstanceOf(DynamicSourceBuilder);
  });

  it('keeps format-specific source options on their format', () => {
    // @ts-expect-error withPage is a PDF option.
    expect(() => client.convert().fromZpl('^XA^XZ').withPage(0)).toThrow(TypeError);
    // @ts-expect-error withSplit is an image or PDF option.
    expect(() => client.convert().fromZpl('^XA^XZ').withSplit({ rows: 2 })).toThrow(TypeError);
    // @ts-expect-error withCommandsToIgnore is a ZPL option.
    expect(() => client.convert().fromPdf(new Uint8Array([1])).withCommandsToIgnore('^PQ')).toThrow(TypeError);
    // @ts-expect-error withPage is a PDF option.
    expect(() => client.convert().fromPng(new Uint8Array([1])).withPage(0)).toThrow(TypeError);
  });

  it('keeps ZPL-specific target options on ZPL', () => {
    const toPdf = client.convert().fromZpl('^XA^XZ').toPdf();
    // @ts-expect-error withLayout is a ZPL target option.
    expect(() => toPdf.withLayout({ across: 2 })).toThrow(TypeError);
    // @ts-expect-error withCollapseCopies is a ZPL target option.
    expect(() => toPdf.withCollapseCopies()).toThrow(TypeError);
  });

  it('offers no target options at the source stage', () => {
    const source = client.convert().fromPdf(new Uint8Array([1]));
    // @ts-expect-error withDpi changes the output, so it is set after to*().
    expect(() => source.withDpi(203)).toThrow(TypeError);
    // @ts-expect-error execute needs a target.
    expect(() => source.execute()).toThrow(TypeError);
  });

  it('offers no source options at the target stage', () => {
    const target = client.convert().fromPdf(new Uint8Array([1])).toZpl();
    // @ts-expect-error withPage changes how the source is read, so it is set before toZpl().
    expect(() => target.withPage(0)).toThrow(TypeError);
    // @ts-expect-error withLabelSize goes into the reader, so it is set before toZpl().
    expect(() => target.withLabelSize(4, 6)).toThrow(TypeError);
  });

  it('sends the same params through the typed builders as the wire expects', async () => {
    let captured: URL | undefined;
    const typed = new LabelZoomClient({
      apiKey: '',
      fetch: (async (input: string | URL) => {
        captured = new URL(String(input));
        return new Response('^XA^XZ', { status: 200, headers: { 'content-type': 'text/plain' } });
      }) as unknown as typeof globalThis.fetch,
    });

    await typed.convert()
      .fromPdf(new Uint8Array([0x25, 0x50, 0x44, 0x46]))
        .withPage(0)
        .withSplit({ rows: 2, columns: 2, skipBlank: true })
      .toZpl()
        .withDpi(203)
        .withCollapseCopies()
        .withLayout({ across: 2, gap: 0.125 })
      .execute();

    expect(captured?.pathname).toBe('/api/v2/convert/pdf/to/zpl');
    expect(JSON.parse(captured?.searchParams.get('params') ?? 'null')).toEqual({
      pdf: { pageNumber: 0 },
      split: { rows: 2, columns: 2, skipBlank: true },
      dpi: 203,
      zpl: { collapseCopies: true },
      layout: { across: 2, gap: 0.125 },
    });
  });

  it('merges a group set in two calls rather than replacing it', async () => {
    let captured: URL | undefined;
    const typed = new LabelZoomClient({
      apiKey: '',
      fetch: (async (input: string | URL) => {
        captured = new URL(String(input));
        return new Response('^XA^XZ', { status: 200, headers: { 'content-type': 'text/plain' } });
      }) as unknown as typeof globalThis.fetch,
    });

    await typed.convert()
      .fromZpl('^XA^XZ')
        .withCommandsToIgnore('^PQ')
      .toZpl()
        .withImageCompression('COMPRESSED_HEX')
      .execute();

    expect(JSON.parse(captured?.searchParams.get('params') ?? 'null')).toEqual({
      zpl: { commandsToIgnore: ['^PQ'], imageCompression: 'COMPRESSED_HEX' },
    });
  });
});
