<?php

declare(strict_types=1);

namespace LabelZoom\Sdk;

use LabelZoom\Sdk\Internal\ConversionDraft;
use LabelZoom\Sdk\Internal\ParameterWriters;

/**
 * The target stage for a format chosen at run time.
 *
 * Static analysis can't know the format here, so every target option is available, named with the
 * format it applies to. `withColorMode()` and `withDarkness()` write the same keys as the source
 * stage's `withRaster()`: the last value set is sent.
 */
final class DynamicTargetBuilder extends TargetStage
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
     * ZPL target: image compression. The server default is {@see ZplImageCompression::Z64}.
     */
    public function withZplImageCompression(ZplImageCompression $compression): self
    {
        ParameterWriters::zplImageCompression($this->draft, $compression);

        return $this;
    }

    /**
     * ZPL target, image or PDF source: each run of identical labels becomes one label with a `^PQ`
     * count.
     */
    public function withZplCollapseCopies(bool $collapse = true): self
    {
        ParameterWriters::zplCollapseCopies($this->draft, $collapse);

        return $this;
    }

    /**
     * Image or PDF source, IMAGE mode: labels side by side in each printed row.
     *
     * Example: `->withLayout(fn (LayoutOptions $l) => $l->across(2)->gap(0.125))`
     *
     * @param callable(LayoutOptions): mixed $configure
     */
    public function withLayout(callable $configure): self
    {
        $configure(new LayoutOptions($this->draft));

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
