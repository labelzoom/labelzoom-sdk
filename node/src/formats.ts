/**
 * Formats the LabelZoom API can convert *from*.
 *
 * A different type from {@link TargetFormat} on purpose. That is what makes `epl`, `tspl` and
 * `dpl` un-selectable as conversion targets: they are source-only on the server, and there is
 * no such member of `TargetFormat` to name.
 */
export type SourceFormat =
  | 'zpl'
  | 'epl'
  | 'tspl'
  | 'dpl'
  | 'xml'
  | 'json'
  | 'pdf'
  | 'png'
  | 'bmp'
  | 'gif'
  | 'jpeg'
  | 'jpg'
  /**
   * A URL, sent as the request body. The *server* then fetches it and converts what it finds.
   * This performs a server-side fetch of a URL you supply — validate it first if it came from
   * untrusted input.
   */
  | 'url';

/**
 * Formats the LabelZoom API can convert *to*.
 *
 * `jpg` and `url` are intentionally absent — `jpg` is an input spelling that normalizes to
 * `jpeg`, and `url` is a fetch instruction rather than a format, so `.to('url')` is a compile
 * error rather than a runtime 404.
 *
 * `epl` and `tspl` output can inline raw binary (EPL `GW`, TSPL `BITMAP`); read `result.bytes`
 * rather than `result.text` for those targets.
 */
export type TargetFormat =
  | 'zpl'
  | 'epl'
  | 'tspl'
  | 'dpl'
  | 'xml'
  | 'json'
  | 'pdf'
  | 'png'
  | 'bmp'
  | 'gif'
  | 'jpeg';

export const SOURCE_FORMATS: readonly SourceFormat[] = [
  'zpl', 'epl', 'tspl', 'dpl', 'xml', 'json', 'pdf', 'png', 'bmp', 'gif', 'jpeg', 'jpg', 'url',
];

export const TARGET_FORMATS: readonly TargetFormat[] = [
  'zpl', 'epl', 'tspl', 'dpl', 'xml', 'json', 'pdf', 'png', 'bmp', 'gif', 'jpeg',
];

/**
 * The one place in the SDK that knows the format matrix.
 *
 * The superseded .NET builder hierarchy spread this across seven classes and they drifted — one
 * handled 2 of 12 sources, another returned the source content type as the target format. Keep
 * it here.
 */
const SOURCE_MEDIA_TYPES: Record<SourceFormat, string> = {
  zpl: 'text/plain',
  epl: 'text/plain',
  tspl: 'text/plain',
  dpl: 'text/plain',
  xml: 'application/xml',
  json: 'application/json',
  pdf: 'application/pdf',
  png: 'image/png',
  bmp: 'image/bmp',
  gif: 'image/gif',
  jpeg: 'image/jpeg',
  jpg: 'image/jpeg',
  url: 'text/plain',
};

/** `jpg` is an alias callers use for files they already have; the wire spelling is `jpeg`. */
export function sourceWireToken(format: SourceFormat): string {
  return format === 'jpg' ? 'jpeg' : format;
}

export function targetWireToken(format: TargetFormat): string {
  return format;
}

export function sourceMediaType(format: SourceFormat): string {
  const mediaType = SOURCE_MEDIA_TYPES[format];
  if (mediaType === undefined) {
    throw new TypeError(`Unknown source format: ${String(format)}`);
  }
  return mediaType;
}

/** Colour handling when rasterizing or tracing images. */
export type ColorMode = 'BW' | 'GRAYSCALE' | 'COLOR';

/** How a source PDF is interpreted. */
export type PdfConversionMode = 'IMAGE' | 'NATIVE';

/** Image compression used when writing ZPL. */
export type ZplImageCompression = 'Z64' | 'COMPRESSED_HEX';
