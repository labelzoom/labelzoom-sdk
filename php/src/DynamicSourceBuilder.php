<?php

declare(strict_types=1);

namespace LabelZoom\Sdk;

use LabelZoom\Sdk\Internal\ParameterWriters;

/**
 * The source stage for a format chosen at run time.
 *
 * Static analysis can't know the format here, so every source option is available, named with the
 * format it applies to. The server ignores an option that doesn't apply to the format.
 */
final class DynamicSourceBuilder extends SourceStage
{
    /**
     * The resolution the source was made for, in dots per inch: how its dots or pixels map to
     * inches. The server default is 203. The output follows it unless the target stage sets its own.
     */
    public function withDpi(int $dpi): self
    {
        ParameterWriters::sourceDpi($this->draft, $dpi);

        return $this;
    }

    /**
     * PDF source: converts one page, by **0-based** index.
     */
    public function withPdfPage(int $zeroBasedPageNumber): self
    {
        ParameterWriters::pdfPage($this->draft, $zeroBasedPageNumber);

        return $this;
    }

    /**
     * PDF source: how it is read. The server default is {@see PdfConversionMode::Image}.
     */
    public function withPdfConversionMode(PdfConversionMode $mode): self
    {
        ParameterWriters::pdfConversionMode($this->draft, $mode);

        return $this;
    }

    /**
     * ZPL source: commands the parser should skip, for example `^PQ`.
     *
     * @param array<array-key, string> $commands
     */
    public function withZplCommandsToIgnore(array $commands): self
    {
        ParameterWriters::zplCommandsToIgnore($this->draft, $commands);

        return $this;
    }

    /**
     * Cuts each page into a grid of labels, read left to right and then down. ZPL target only.
     *
     * Example: `->withSplit(fn (SplitOptions $s) => $s->rows(2)->columns(2)->skipBlank())`
     *
     * @param callable(SplitOptions): mixed $configure
     */
    public function withSplit(callable $configure): self
    {
        $configure(new SplitOptions($this->draft));

        return $this;
    }

    /**
     * How the picture is rasterized: scaling, its position on the label (which, with a label size,
     * crops it), the black-and-white threshold and the color reduction.
     *
     * Example: `->withRaster(fn (RasterOptions $r) => $r->scaling(150)->darkness(60))`
     *
     * @param callable(RasterOptions): mixed $configure
     */
    public function withRaster(callable $configure): self
    {
        $configure(new RasterOptions($this->draft));

        return $this;
    }
}
