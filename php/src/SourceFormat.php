<?php

declare(strict_types=1);

namespace LabelZoom\Sdk;

/**
 * A format the LabelZoom API can convert *from*.
 *
 * Deliberately a different type from {@see TargetFormat}. That is what makes EPL, TSPL and DPL
 * un-selectable as conversion targets: they are source-only on the server, and there is simply
 * no `TargetFormat::Epl` to name. Passing one where a target is expected is a `TypeError`, and
 * PHPStan rejects it before the code ever runs.
 *
 * The backing value is the token a *caller* would write, so `Jpg` and `Jpeg` are distinct cases.
 * {@see self::wireToken()} is what goes in the URL, and it normalizes `jpg` to `jpeg` (rule A2).
 */
enum SourceFormat: string
{
    /** Zebra Programming Language. */
    case Zpl = 'zpl';

    /** Eltron Programming Language. Source-only. */
    case Epl = 'epl';

    /** TSC Printer Language. Source-only. */
    case Tspl = 'tspl';

    /** Datamax Printer Language. Source-only. */
    case Dpl = 'dpl';

    /** LabelZoom XML. */
    case Xml = 'xml';

    /** LabelZoom JSON. */
    case Json = 'json';

    /** PDF document. */
    case Pdf = 'pdf';

    /** PNG image. */
    case Png = 'png';

    /** BMP image. */
    case Bmp = 'bmp';

    /** GIF image. */
    case Gif = 'gif';

    /** JPEG image. */
    case Jpeg = 'jpeg';

    /** Alias for {@see self::Jpeg}; normalized to `jpeg` on the wire. */
    case Jpg = 'jpg';

    /**
     * A URL, sent as the request body. The *server* then fetches it and converts whatever it finds.
     *
     * This performs a server-side fetch of a URL you supply. Validate it first if it came from
     * untrusted input.
     */
    case Url = 'url';

    /**
     * The token used in the request path.
     *
     * `Jpg` and `Jpeg` both yield `jpeg`: the server knows one spelling, and normalizing here
     * means no call site has to remember which one (rule A2).
     */
    public function wireToken(): string
    {
        return $this === self::Jpg ? 'jpeg' : $this->value;
    }

    /**
     * The `Content-Type` a request carrying this format must send.
     *
     * The format metadata table lives here and only here (contract §1.2) — never inlined at a
     * call site, where the twelve copies would drift.
     */
    public function mediaType(): string
    {
        return match ($this) {
            self::Zpl, self::Epl, self::Tspl, self::Dpl, self::Url => 'text/plain',
            self::Xml => 'application/xml',
            self::Json => 'application/json',
            self::Pdf => 'application/pdf',
            self::Png => 'image/png',
            self::Bmp => 'image/bmp',
            self::Gif => 'image/gif',
            self::Jpeg, self::Jpg => 'image/jpeg',
        };
    }
}
