<?php

declare(strict_types=1);

namespace LabelZoom\Sdk;

/**
 * A format the LabelZoom API can convert *to*.
 *
 * EPL, TSPL and DPL are intentionally absent — the server accepts them as sources only, so asking
 * for one fails statically rather than as a runtime 404. There is no `TargetFormat::Epl` to write.
 */
enum TargetFormat: string
{
    /** Zebra Programming Language. All labels are concatenated. */
    case Zpl = 'zpl';

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
