<?php

declare(strict_types=1);

namespace LabelZoom\Sdk;

use LabelZoom\Sdk\Internal\ConversionDraft;

/**
 * Puts labels side by side in each printed row, for multi-column roll stock. Configured inside
 * `withLayout(fn (LayoutOptions $l) => ...)`.
 *
 * Applies to image or PDF to ZPL in IMAGE mode. The response's `^PW` is the width of the whole
 * row, so the label size stays the size of **one** label.
 */
final class LayoutOptions
{
    /** @internal */
    public function __construct(private readonly ConversionDraft $draft)
    {
    }

    /** Labels across the roll, 1 to 10. The server default is 1. */
    public function across(int $across): self
    {
        $this->draft->nested('layout', 'across', $across);

        return $this;
    }

    /** Space between two labels in a row, **in inches**. The server default is 0. */
    public function gap(float $gapInches): self
    {
        $this->draft->nested('layout', 'gap', $gapInches);

        return $this;
    }

    /** How labels fill the columns of a row. The server default is {@see LayoutFill::Sequential}. */
    public function fill(LayoutFill $fill): self
    {
        $this->draft->nested('layout', 'fill', $fill->value);

        return $this;
    }
}
