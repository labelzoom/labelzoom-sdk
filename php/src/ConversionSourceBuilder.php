<?php

declare(strict_types=1);

namespace LabelZoom\Sdk;

/**
 * Chooses the target format. One class covers all eight.
 *
 * There is no `toEpl()`, `toTspl()` or `toDpl()`, and there never will be: those formats are
 * source-only on the server, and {@see TargetFormat} has no case for them. Attempting one fails
 * static analysis rather than producing a runtime 404.
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
