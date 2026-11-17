<?php

declare(strict_types=1);

namespace LabelZoom\Sdk;

use LabelZoom\Sdk\Internal\ParameterWriters;

/**
 * The source stage for a PDF document. A PDF has no dots, so there is no `withDpi()`.
 */
final class PdfSourceBuilder extends SourceStage
{
    /**
     * Converts one page, by **0-based** index. Omit this to convert every page.
     */
    public function withPage(int $zeroBasedPageNumber): self
    {
        ParameterWriters::pdfPage($this->draft, $zeroBasedPageNumber);

        return $this;
    }

    /**
     * How the PDF is read. The server default is {@see PdfConversionMode::Image}, which splitting
     * and layout require.
     */
    public function withConversionMode(PdfConversionMode $mode): self
    {
        ParameterWriters::pdfConversionMode($this->draft, $mode);

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
