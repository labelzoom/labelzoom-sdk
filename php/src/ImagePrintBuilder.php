<?php

declare(strict_types=1);

namespace LabelZoom\Sdk;

use LabelZoom\Sdk\Internal\ParameterWriters;

/**
 * Prints a PNG, BMP, GIF or JPEG image.
 *
 * The print twin of {@see ImageSourceBuilder}.
 */
final class ImagePrintBuilder extends PrintStage
{
    /**
     * The image's resolution, in dots per inch: how its pixels map to inches. The server default is
     * 203.
     */
    public function withDpi(int $dpi): self
    {
        ParameterWriters::sourceDpi($this->draft, $dpi);

        return $this;
    }

    /**
     * Cuts each page into a grid of labels, read left to right and then down.
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
