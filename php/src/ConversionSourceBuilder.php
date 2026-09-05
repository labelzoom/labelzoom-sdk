<?php

declare(strict_types=1);

namespace LabelZoom\Sdk;

/**
 * Chooses the target format. One class covers all thirteen.
 *
 * There is no `toUrl()`: `url` is a source-only fetch instruction, and {@see TargetFormat} has no
 * case for it. Attempting one fails static analysis rather than producing a runtime 404.
 */
final class ConversionSourceBuilder
{
    /** @internal */
    public function __construct(
        private readonly LabelZoomClient $client,
        private readonly SourceFormat $source,
        private readonly string $body,
        private readonly string $contentType,
    ) {
    }

    /** Selects the target format. */
    public function to(TargetFormat $target): ConversionTargetBuilder
    {
        return new ConversionTargetBuilder($this->client, $this->source, $target, $this->body, $this->contentType);
    }

    /** Converts to ZPL. All labels are concatenated into one document. */
    public function toZpl(): ConversionTargetBuilder
    {
        return $this->to(TargetFormat::Zpl);
    }

    /**
     * Converts to EPL. All labels are concatenated into one document. Read `getBytes()` rather
     * than `getText()`: EPL's `GW` command inlines raw binary.
     */
    public function toEpl(): ConversionTargetBuilder
    {
        return $this->to(TargetFormat::Epl);
    }

    /**
     * Converts to Intermec IPL. All labels are concatenated into one document. As with
     * {@see self::toEpl()}, prefer `getBytes()`: commands are framed in `STX`/`ETX`.
     */
    public function toIpl(): ConversionTargetBuilder
    {
        return $this->to(TargetFormat::Ipl);
    }

    /**
     * Converts to TSPL. All labels are concatenated into one document. As with {@see self::toEpl()},
     * prefer `getBytes()`.
     */
    public function toTspl(): ConversionTargetBuilder
    {
        return $this->to(TargetFormat::Tspl);
    }

    /** Converts to Datamax DPL. All labels are concatenated into one document. */
    public function toDpl(): ConversionTargetBuilder
    {
        return $this->to(TargetFormat::Dpl);
    }

    /**
     * Converts to SATO SBPL. All labels are concatenated into one document. As with
     * {@see self::toEpl()}, prefer `getBytes()`: every command is framed with `ESC`.
     */
    public function toSbpl(): ConversionTargetBuilder
    {
        return $this->to(TargetFormat::Sbpl);
    }

    /** Converts to LabelZoom XML. Returns the first label only. */
    public function toXml(): ConversionTargetBuilder
    {
        return $this->to(TargetFormat::Xml);
    }

    /** Converts to LabelZoom JSON. First label only; requires a paid license. */
    public function toJson(): ConversionTargetBuilder
    {
        return $this->to(TargetFormat::Json);
    }

    /** Converts to PDF, one page per label. */
    public function toPdf(): ConversionTargetBuilder
    {
        return $this->to(TargetFormat::Pdf);
    }

    /** Converts to a PNG image. Returns the first label only. */
    public function toPng(): ConversionTargetBuilder
    {
        return $this->to(TargetFormat::Png);
    }

    /** Converts to a BMP image. Returns the first label only. */
    public function toBmp(): ConversionTargetBuilder
    {
        return $this->to(TargetFormat::Bmp);
    }

    /** Converts to a GIF image. Returns the first label only. */
    public function toGif(): ConversionTargetBuilder
    {
        return $this->to(TargetFormat::Gif);
    }

    /** Converts to a JPEG image. Returns the first label only. */
    public function toJpeg(): ConversionTargetBuilder
    {
        return $this->to(TargetFormat::Jpeg);
    }
}
