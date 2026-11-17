<?php

declare(strict_types=1);

namespace LabelZoom\Sdk;

use LabelZoom\Sdk\Internal\ConversionDraft;
use LabelZoom\Sdk\Internal\ParameterWriters;

/**
 * The target stage for a PNG, BMP, GIF or JPEG image.
 */
final class ImageTargetBuilder extends TargetStage
{
    /** @internal */
    public function __construct(ConversionDraft $draft, TargetFormat $target)
    {
        parent::__construct($draft, $target);
    }

    /**
     * The resolution of the output, in dots per inch. The server default is the source's
     * resolution, else 203. To move a label to a printer of another density at the same physical
     * size, set the source's resolution too.
     */
    public function withDpi(int $dpi): self
    {
        ParameterWriters::targetDpi($this->draft, $dpi);

        return $this;
    }

    /**
     * {@see ColorMode::Bw} renders a black-and-white image at the `withDarkness()` threshold; any
     * other mode renders in color, the default.
     */
    public function withColorMode(ColorMode $mode): self
    {
        ParameterWriters::colorMode($this->draft, $mode);

        return $this;
    }

    /**
     * Luminance threshold from 0 to 100 for a black-and-white image. The server default is 70.
     */
    public function withDarkness(int $darkness): self
    {
        ParameterWriters::darkness($this->draft, $darkness);

        return $this;
    }
}
