import type { ConversionBody, ConversionResult, LabelZoomClient } from './client.js';
import type {
  ColorMode,
  PdfConversionMode,
  SourceFormat,
  TargetFormat,
  ZplImageCompression,
} from './formats.js';
import type { ConversionOptions, DataRecord } from './options.js';

/** Cuts each page into a grid of labels. Every length is in **inches**. */
export type SplitOptions = NonNullable<ConversionOptions['split']>;

/** Puts labels side by side in each printed row. The gap is in **inches**. */
export type LayoutOptions = NonNullable<ConversionOptions['layout']>;

/**
 * How an image or PDF source is rasterized before it becomes labels. These act on the source picture, so they sit
 * with `withSplit` and the label size, which cut and frame the same picture. Rotation turns the finished label for
 * every format, so it is a target option.
 */
export interface RasterOptions {
  /** Percent. Server default 100. A PDF is rendered larger or smaller; an image is resampled. */
  scaling?: number;
  /**
   * Where the picture sits on its label, in output dots from the label's top-left corner. A negative offset crops
   * that much off the picture's left or top, so with a label size this is how a picture is cropped. Printer-language
   * targets only.
   */
  position?: { x: number; y: number };
  /** Luminance threshold 0–100 for black and white. Server default 70. Also decides what `split.skipBlank` drops. */
  darkness?: number;
  /** Server default `GRAYSCALE`, which dithers the picture. */
  colorMode?: ColorMode;
}

/**
 * The request under construction, handed from the source stage to the target stage. Both stages
 * write into the same options object, because the wire has one `?params=` object, not one per
 * stage. The stages only decide which options a caller can reach at each point in the chain.
 */
interface Draft {
  readonly client: LabelZoomClient;
  readonly source: SourceFormat;
  readonly body: ConversionBody;
  readonly asBase64Text: boolean;
  readonly options: ConversionOptions;
}

/**
 * Chooses the source document. Returned by `client.convert()`.
 *
 * A named `from*` method returns a source stage typed for that format, so only the options that
 * apply to it are offered: `fromPdf` returns a {@link PdfSourceBuilder} with `withPage`, and
 * `fromZpl` doesn't. {@link from} and {@link fromBase64Text} pick the format at run time and return
 * a {@link DynamicSourceBuilder}, which offers every source option.
 */
export class ConversionRequestBuilder {
  readonly #client: LabelZoomClient;

  constructor(client: LabelZoomClient) {
    this.#client = client;
  }

  from(format: SourceFormat, body: ConversionBody): DynamicSourceBuilder {
    return new DynamicSourceBuilder(this.#draft(format, body, false));
  }

  /** Uses a base64-encoded document, sent as `text/plain`. */
  fromBase64Text(format: SourceFormat, base64: string): DynamicSourceBuilder {
    return new DynamicSourceBuilder(this.#draft(format, base64, true));
  }

  fromZpl(zpl: ConversionBody): ZplSourceBuilder { return new ZplSourceBuilder(this.#draft('zpl', zpl)); }
  fromEpl(epl: ConversionBody): SourceBuilder { return new SourceBuilder(this.#draft('epl', epl)); }
  fromIpl(ipl: ConversionBody): SourceBuilder { return new SourceBuilder(this.#draft('ipl', ipl)); }
  fromTspl(tspl: ConversionBody): SourceBuilder { return new SourceBuilder(this.#draft('tspl', tspl)); }
  fromDpl(dpl: ConversionBody): SourceBuilder { return new SourceBuilder(this.#draft('dpl', dpl)); }
  fromSbpl(sbpl: ConversionBody): SourceBuilder { return new SourceBuilder(this.#draft('sbpl', sbpl)); }
  fromXml(xml: ConversionBody): SourceBuilder { return new SourceBuilder(this.#draft('xml', xml)); }
  fromJson(json: ConversionBody): SourceBuilder { return new SourceBuilder(this.#draft('json', json)); }
  fromPdf(pdf: ConversionBody): PdfSourceBuilder { return new PdfSourceBuilder(this.#draft('pdf', pdf)); }
  fromPng(png: ConversionBody): ImageSourceBuilder { return new ImageSourceBuilder(this.#draft('png', png)); }
  fromBmp(bmp: ConversionBody): ImageSourceBuilder { return new ImageSourceBuilder(this.#draft('bmp', bmp)); }
  fromGif(gif: ConversionBody): ImageSourceBuilder { return new ImageSourceBuilder(this.#draft('gif', gif)); }
  fromJpeg(jpeg: ConversionBody): ImageSourceBuilder { return new ImageSourceBuilder(this.#draft('jpeg', jpeg)); }

  /**
   * Has the *server* fetch a URL and convert what it finds. Validate the URL first if it came
   * from untrusted input.
   */
  fromUrl(url: string): DynamicSourceBuilder { return this.from('url', url); }

  #draft(source: SourceFormat, body: ConversionBody, asBase64Text = false): Draft {
    return { client: this.#client, source, body, asBase64Text, options: {} };
  }
}

/**
 * The source stage: options that change how the source document is **read**, followed by the
 * choice of target format.
 *
 * The options here apply to every source format. Format-specific ones live on the builder that
 * `from*` returns for that format, such as {@link PdfSourceBuilder.withPage}.
 */
export abstract class SourceStage {
  protected readonly draft: Draft;

  protected constructor(draft: Draft) {
    this.draft = draft;
  }

  /** **Inches**, not dots. Replaces the size the source declares; omit to use that size. */
  withLabelSize(widthInches: number, heightInches: number): this {
    this.draft.options.label = { width: widthInches, height: heightInches };
    return this;
  }

  /**
   * **0-based** index of the one label to convert, on every plan. For an image or PDF it counts
   * labels after any split, page by page.
   */
  withLabelIndex(zeroBasedIndex: number): this {
    this.draft.options.labelIndex = zeroBasedIndex;
    return this;
  }

  /** One label per record. A single record means a single label. */
  withData(...records: DataRecord[]): this {
    this.draft.options.data = records;
    return this;
  }

  /** Selects the target format at run time. Every target option is available. */
  to(target: TargetFormat): DynamicTargetBuilder { return new DynamicTargetBuilder(this.draft, target); }

  toZpl(): ZplTargetBuilder { return new ZplTargetBuilder(this.draft); }
  /** EPL output can inline raw binary (`GW`); prefer `result.bytes` over `result.text`. */
  toEpl(): TargetBuilder { return new TargetBuilder(this.draft, 'epl'); }
  /** IPL frames commands in `STX`/`ETX`; prefer `result.bytes` over `result.text`. */
  toIpl(): TargetBuilder { return new TargetBuilder(this.draft, 'ipl'); }
  /** TSPL output can inline raw binary (`BITMAP`); prefer `result.bytes` over `result.text`. */
  toTspl(): TargetBuilder { return new TargetBuilder(this.draft, 'tspl'); }
  toDpl(): TargetBuilder { return new TargetBuilder(this.draft, 'dpl'); }
  /** SBPL frames every command with `ESC`; prefer `result.bytes` over `result.text`. */
  toSbpl(): TargetBuilder { return new TargetBuilder(this.draft, 'sbpl'); }
  toXml(): TargetBuilder { return new TargetBuilder(this.draft, 'xml'); }
  toJson(): TargetBuilder { return new TargetBuilder(this.draft, 'json'); }
  toPdf(): TargetBuilder { return new TargetBuilder(this.draft, 'pdf'); }
  toPng(): ImageTargetBuilder { return new ImageTargetBuilder(this.draft, 'png'); }
  toBmp(): ImageTargetBuilder { return new ImageTargetBuilder(this.draft, 'bmp'); }
  toGif(): ImageTargetBuilder { return new ImageTargetBuilder(this.draft, 'gif'); }
  toJpeg(): ImageTargetBuilder { return new ImageTargetBuilder(this.draft, 'jpeg'); }
}

/** The source stage for EPL, IPL, TSPL, DPL, SBPL, LabelZoom XML and LabelZoom JSON. */
export class SourceBuilder extends SourceStage {
  /** @internal */
  constructor(draft: Draft) { super(draft); }
}

/** The source stage for a ZPL document. */
export class ZplSourceBuilder extends SourceStage {
  /** @internal */
  constructor(draft: Draft) { super(draft); }

  /** ZPL commands the parser should skip, for example `'^PQ'`. */
  withCommandsToIgnore(...commands: string[]): this {
    setZpl(this.draft, { commandsToIgnore: commands });
    return this;
  }
}

/** The source stage for a PNG, BMP, GIF or JPEG image. */
export class ImageSourceBuilder extends SourceStage {
  /** @internal */
  constructor(draft: Draft) { super(draft); }

  /**
   * Cuts the image into a grid of labels, read left to right and then down. ZPL target only.
   *
   * @example `.withSplit({ rows: 2, columns: 2, skipBlank: true })`
   */
  withSplit(split: SplitOptions): this {
    setSplit(this.draft, split);
    return this;
  }

  /**
   * How the image is rasterized: scaling, its position on the label (which, with a label size, crops it), the
   * black-and-white threshold and the color reduction.
   *
   * @example `.withRaster({ scaling: 150, position: { x: -20, y: 0 }, darkness: 60 })`
   */
  withRaster(raster: RasterOptions): this {
    setRaster(this.draft, raster);
    return this;
  }
}

/** The source stage for a PDF document. */
export class PdfSourceBuilder extends SourceStage {
  /** @internal */
  constructor(draft: Draft) { super(draft); }

  /** **0-based**. Omit to convert every page. */
  withPage(zeroBasedPageNumber: number): this {
    setPdf(this.draft, { pageNumber: zeroBasedPageNumber });
    return this;
  }

  /** Server default `IMAGE`, which splitting and layout require. */
  withConversionMode(mode: PdfConversionMode): this {
    setPdf(this.draft, { conversionMode: mode });
    return this;
  }

  /**
   * Cuts each page into a grid of labels, such as a Letter sheet of 30, read left to right and
   * then down. ZPL target only.
   *
   * @example `.withSplit({ rows: 10, columns: 3, left: 0.19, top: 0.5 })`
   */
  withSplit(split: SplitOptions): this {
    setSplit(this.draft, split);
    return this;
  }

  /**
   * How each page is rasterized: scaling, its position on the label (which, with a label size, crops it), the
   * black-and-white threshold and the color reduction. In `NATIVE` mode the page isn't rasterized, and only scaling
   * applies.
   */
  withRaster(raster: RasterOptions): this {
    setRaster(this.draft, raster);
    return this;
  }
}

/**
 * The source stage for a format chosen at run time. The compiler can't know the format here, so
 * every source option is available, named with the format it applies to. The server ignores an
 * option that doesn't apply to the format.
 */
export class DynamicSourceBuilder extends SourceStage {
  /** @internal */
  constructor(draft: Draft) { super(draft); }

  /** **0-based**. Omit to convert every page. */
  withPdfPage(zeroBasedPageNumber: number): this {
    setPdf(this.draft, { pageNumber: zeroBasedPageNumber });
    return this;
  }

  withPdfConversionMode(mode: PdfConversionMode): this {
    setPdf(this.draft, { conversionMode: mode });
    return this;
  }

  withZplCommandsToIgnore(...commands: string[]): this {
    setZpl(this.draft, { commandsToIgnore: commands });
    return this;
  }

  /** Image or PDF to ZPL, `IMAGE` mode: cuts each page into a grid of labels. */
  withSplit(split: SplitOptions): this {
    setSplit(this.draft, split);
    return this;
  }

  /** Image or PDF source: how it is rasterized. See {@link PdfSourceBuilder.withRaster}. */
  withRaster(raster: RasterOptions): this {
    setRaster(this.draft, raster);
    return this;
  }
}

/**
 * The target stage: options that change the **output**, followed by `execute`.
 *
 * The options here apply to every target. Format-specific ones live on {@link ZplTargetBuilder} and
 * {@link ImageTargetBuilder}; how an image or PDF source is rasterized is a source option ({@link RasterOptions}).
 * An option the server doesn't apply to the chosen format pair is ignored rather than rejected.
 */
export abstract class TargetStage {
  protected readonly draft: Draft;
  readonly #target: TargetFormat;

  protected constructor(draft: Draft, target: TargetFormat) {
    this.draft = draft;
    this.#target = target;
  }

  withDpi(dpi: number): this { this.draft.options.dpi = dpi; return this; }
  /** Must be a multiple of 90; rejected locally otherwise. */
  withRotation(rotation: number): this { this.draft.options.rotation = rotation; return this; }
  withWatermark(watermark = true): this { this.draft.options.watermark = watermark; return this; }
  /** e.g. `moca`. Requires a paid license. */
  withDialect(dialect: string): this { this.draft.options.dialect = dialect; return this; }

  /** Escape hatch for anything not modeled yet. Unknown keys are ignored server-side. */
  withParameter(key: string, value: unknown): this {
    this.draft.options[key] = value;
    return this;
  }

  /**
   * Executes the conversion. The options are copied when this is called, so a builder can be
   * executed again, or changed and executed again, without affecting a request in flight.
   */
  execute(signal?: AbortSignal): Promise<ConversionResult> {
    return this.draft.client.convert({
      from: this.draft.source,
      to: this.#target,
      body: this.draft.body,
      options: structuredClone(this.draft.options),
      asBase64Text: this.draft.asBase64Text,
      signal,
    });
  }
}

/** The target stage for EPL, IPL, TSPL, DPL, SBPL, LabelZoom XML and JSON, and PDF. */
export class TargetBuilder extends TargetStage {
  /** @internal */
  constructor(draft: Draft, target: TargetFormat) { super(draft, target); }
}

/** The target stage for a PNG, BMP, GIF or JPEG image. */
export class ImageTargetBuilder extends TargetStage {
  /** @internal */
  constructor(draft: Draft, target: TargetFormat) { super(draft, target); }

  /** `BW` renders a black-and-white image at the `withDarkness` threshold. Anything else renders in color, the default. */
  withColorMode(mode: ColorMode): this { this.draft.options.colorMode = mode; return this; }
  /** Luminance threshold 0–100 for a black-and-white image. Server default 70. */
  withDarkness(darkness: number): this { this.draft.options.darkness = darkness; return this; }
}

/** The target stage for ZPL output. */
export class ZplTargetBuilder extends TargetStage {
  /** @internal */
  constructor(draft: Draft) { super(draft, 'zpl'); }

  /** Server default `Z64`. */
  withImageCompression(compression: ZplImageCompression): this {
    setZpl(this.draft, { imageCompression: compression });
    return this;
  }

  /**
   * Image or PDF source: each run of identical labels becomes one label with a `^PQ` count.
   * Server default `false`, because this changes the number of labels in the output.
   */
  withCollapseCopies(collapse = true): this {
    setZpl(this.draft, { collapseCopies: collapse });
    return this;
  }

  /**
   * Image or PDF source, `IMAGE` mode: labels side by side in each printed row, for multi-column
   * roll stock.
   *
   * @example `.withLayout({ across: 2, gap: 0.125 })`
   */
  withLayout(layout: LayoutOptions): this {
    setLayout(this.draft, layout);
    return this;
  }
}

/**
 * The target stage for a format chosen at run time. The compiler can't know the format here, so
 * every target option is available, named with the format it applies to.
 */
export class DynamicTargetBuilder extends TargetStage {
  /** @internal */
  constructor(draft: Draft, target: TargetFormat) { super(draft, target); }

  withZplImageCompression(compression: ZplImageCompression): this {
    setZpl(this.draft, { imageCompression: compression });
    return this;
  }

  withZplCollapseCopies(collapse = true): this {
    setZpl(this.draft, { collapseCopies: collapse });
    return this;
  }

  /** Image or PDF to ZPL, `IMAGE` mode: labels side by side in each printed row. */
  withLayout(layout: LayoutOptions): this {
    setLayout(this.draft, layout);
    return this;
  }

  /**
   * See {@link ImageTargetBuilder.withColorMode}. This writes the same `colorMode` as `withRaster` on the source
   * stage, so set it once: the last call wins.
   */
  withColorMode(mode: ColorMode): this { this.draft.options.colorMode = mode; return this; }
  /** See {@link ImageTargetBuilder.withDarkness}. As with `withColorMode`, the source stage writes the same key. */
  withDarkness(darkness: number): this { this.draft.options.darkness = darkness; return this; }
}

// Nested groups are merged, never replaced (contract §4.1): `zpl` holds both a source option
// (`commandsToIgnore`) and target options, and `split`/`layout` can be set in more than one call.

function setPdf(draft: Draft, pdf: NonNullable<ConversionOptions['pdf']>): void {
  draft.options.pdf = { ...draft.options.pdf, ...pdf };
}

function setZpl(draft: Draft, zpl: NonNullable<ConversionOptions['zpl']>): void {
  draft.options.zpl = { ...draft.options.zpl, ...zpl };
}

function setSplit(draft: Draft, split: SplitOptions): void {
  draft.options.split = { ...draft.options.split, ...split };
}

/** Raster options are top-level keys on the wire; only the ones given are written. */
function setRaster(draft: Draft, raster: RasterOptions): void {
  if (raster.scaling !== undefined) draft.options.scaling = raster.scaling;
  if (raster.position !== undefined) draft.options.position = raster.position;
  if (raster.darkness !== undefined) draft.options.darkness = raster.darkness;
  if (raster.colorMode !== undefined) draft.options.colorMode = raster.colorMode;
}

function setLayout(draft: Draft, layout: LayoutOptions): void {
  draft.options.layout = { ...draft.options.layout, ...layout };
}
