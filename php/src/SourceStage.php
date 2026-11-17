<?php

declare(strict_types=1);

namespace LabelZoom\Sdk;

use LabelZoom\Sdk\Internal\ConversionDraft;
use LabelZoom\Sdk\Internal\ParameterWriters;

/**
 * The source stage: options that change how the source document is **read**, followed by the
 * choice of target format.
 *
 * A conversion is built in two stages. `from*` picks the source and returns a source-stage builder;
 * `to*` picks the target and returns a target-stage builder that executes the request. Each stage
 * offers only the options that act on its side, and a named `from*` offers only the options for
 * that format.
 */
abstract class SourceStage
{
    /** @internal */
    public function __construct(protected readonly ConversionDraft $draft)
    {
    }

    /**
     * Label dimensions **in inches**. They replace whatever size the source declares; omit this to
     * use the declared size, or to detect one from the content.
     *
     * Inches -- not dots, not millimeters.
     */
    public function withLabelSize(float $widthInches, float $heightInches): static
    {
        ParameterWriters::labelSize($this->draft, $widthInches, $heightInches);

        return $this;
    }

    /**
     * Converts only one label of a multi-label source, on every plan, identified by a **0-based**
     * index. For an image or PDF it counts labels after any split, page by page.
     */
    public function withLabelIndex(int $zeroBasedIndex): static
    {
        ParameterWriters::labelIndex($this->draft, $zeroBasedIndex);

        return $this;
    }

    /**
     * Supplies data to fill the label's variable fields. **Each record produces one label.**
     *
     * A single record may be passed on its own; it is wrapped into a one-element array rather than
     * rejected (rule C3).
     *
     * @param array<string, mixed>|list<array<string, mixed>> $records
     */
    public function withData(array $records): static
    {
        ParameterWriters::data($this->draft, $records);

        return $this;
    }

    /** Selects the target format. */
    public function to(TargetFormat $target): DynamicTargetBuilder
    {
        return new DynamicTargetBuilder($this->draft, $target);
    }

    /** Converts to ZPL. All labels are concatenated into one document. */
    public function toZpl(): ZplTargetBuilder
    {
        return new ZplTargetBuilder($this->draft);
    }

    /**
     * Converts to EPL. All labels are concatenated into one document. Read `getBytes()` rather
     * than `getText()`: EPL's `GW` command inlines raw binary.
     */
    public function toEpl(): TargetBuilder
    {
        return new TargetBuilder($this->draft, TargetFormat::Epl);
    }

    /**
     * Converts to Intermec IPL. All labels are concatenated into one document. As with
     * {@see self::toEpl()}, prefer `getBytes()`: commands are framed in `STX`/`ETX`.
     */
    public function toIpl(): TargetBuilder
    {
        return new TargetBuilder($this->draft, TargetFormat::Ipl);
    }

    /**
     * Converts to TSPL. All labels are concatenated into one document. As with {@see self::toEpl()},
     * prefer `getBytes()`.
     */
    public function toTspl(): TargetBuilder
    {
        return new TargetBuilder($this->draft, TargetFormat::Tspl);
    }

    /** Converts to Datamax DPL. All labels are concatenated into one document. */
    public function toDpl(): TargetBuilder
    {
        return new TargetBuilder($this->draft, TargetFormat::Dpl);
    }

    /**
     * Converts to SATO SBPL. All labels are concatenated into one document. As with
     * {@see self::toEpl()}, prefer `getBytes()`: every command is framed with `ESC`.
     */
    public function toSbpl(): TargetBuilder
    {
        return new TargetBuilder($this->draft, TargetFormat::Sbpl);
    }

    /**
     * Converts to Zebra CPCL, for Zebra's mobile printers. All labels are concatenated into one
     * document. Graphics are written as hex, so the output is plain text.
     */
    public function toCpcl(): TargetBuilder
    {
        return new TargetBuilder($this->draft, TargetFormat::Cpcl);
    }

    /** Converts to LabelZoom XML. Returns the first label only. */
    public function toXml(): TargetBuilder
    {
        return new TargetBuilder($this->draft, TargetFormat::Xml);
    }

    /** Converts to LabelZoom JSON. First label only; requires a paid license. */
    public function toJson(): TargetBuilder
    {
        return new TargetBuilder($this->draft, TargetFormat::Json);
    }

    /** Converts to PDF, one page per label. */
    public function toPdf(): PdfTargetBuilder
    {
        return new PdfTargetBuilder($this->draft);
    }

    /** Converts to a PNG image. Returns the first label only. */
    public function toPng(): ImageTargetBuilder
    {
        return new ImageTargetBuilder($this->draft, TargetFormat::Png);
    }

    /** Converts to a BMP image. Returns the first label only. */
    public function toBmp(): ImageTargetBuilder
    {
        return new ImageTargetBuilder($this->draft, TargetFormat::Bmp);
    }

    /** Converts to a GIF image. Returns the first label only. */
    public function toGif(): ImageTargetBuilder
    {
        return new ImageTargetBuilder($this->draft, TargetFormat::Gif);
    }

    /** Converts to a JPEG image. Returns the first label only. */
    public function toJpeg(): ImageTargetBuilder
    {
        return new ImageTargetBuilder($this->draft, TargetFormat::Jpeg);
    }
}
