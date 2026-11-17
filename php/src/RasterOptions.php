<?php

declare(strict_types=1);

namespace LabelZoom\Sdk;

use LabelZoom\Sdk\Internal\ConversionDraft;
use LabelZoom\Sdk\Internal\ParameterWriters;

/**
 * How an image or PDF source is rasterized before it becomes labels. Configured inside
 * `withRaster(fn (RasterOptions $r) => ...)`.
 *
 * These act on the source picture, so they sit with `withSplit` and the label size, which cut and
 * frame the same picture. Rotation turns the finished label for every format, so it is a target
 * option.
 */
final class RasterOptions
{
    /** @internal */
    public function __construct(private readonly ConversionDraft $draft)
    {
    }

    /** Scales the picture, as a percentage. The server default is 100. */
    public function scaling(float $percent): self
    {
        ParameterWriters::scaling($this->draft, $percent);

        return $this;
    }

    /**
     * Where the picture sits on its label, in output dots from the label's top-left corner. A
     * negative offset crops that much off its left or top, so with a label size this is how a
     * picture is cropped. Printer-language targets only.
     */
    public function position(int $x, int $y): self
    {
        ParameterWriters::position($this->draft, $x, $y);

        return $this;
    }

    /**
     * Luminance threshold from 0 to 100 for reducing the picture to black and white. The server
     * default is 70. It also decides which labels `SplitOptions::skipBlank()` drops.
     */
    public function darkness(int $darkness): self
    {
        ParameterWriters::darkness($this->draft, $darkness);

        return $this;
    }

    /** How the picture's color is reduced. The server default is {@see ColorMode::Grayscale}. */
    public function colorMode(ColorMode $mode): self
    {
        ParameterWriters::colorMode($this->draft, $mode);

        return $this;
    }
}
