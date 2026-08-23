<?php

declare(strict_types=1);

namespace LabelZoom\Sdk;

/**
 * A format the LabelZoom API can convert *to*.
 *
 * `Jpg` and `Url` are intentionally absent: `jpg` is an input spelling that normalizes to
 * {@see self::Jpeg}, and `url` is a fetch instruction rather than a format, so asking for one
 * fails statically rather than as a runtime 404. There is no `TargetFormat::Url` to write.
 */
enum TargetFormat: string
{
    /** Zebra Programming Language. All labels are concatenated. */
    case Zpl = 'zpl';

    /**
     * Eltron Programming Language. All labels are concatenated.
     *
     * Read `getBytes()` rather than `getText()`: EPL's `GW` graphics command inlines raw binary
     * that a charset decode can corrupt.
     */
    case Epl = 'epl';

    /**
     * TSC printer language. All labels are concatenated. As with {@see self::Epl}, the `BITMAP`
     * command inlines raw binary, so prefer `getBytes()`.
     */
    case Tspl = 'tspl';

    /** Datamax Printer Language. All labels are concatenated. */
    case Dpl = 'dpl';

    /** LabelZoom XML. First label only. */
    case Xml = 'xml';

    /** LabelZoom JSON. First label only. Requires a paid license. */
    case Json = 'json';

    /** PDF document, one page per label. */
    case Pdf = 'pdf';

    /** PNG image. First label only. */
    case Png = 'png';

    /** BMP image. First label only. */
    case Bmp = 'bmp';

    /** GIF image. First label only. */
    case Gif = 'gif';

    /** JPEG image. First label only. */
    case Jpeg = 'jpeg';

    /** The token used in the request path. */
    public function wireToken(): string
    {
        return $this->value;
    }
}
