<?php

declare(strict_types=1);

namespace LabelZoom\Sdk;

use LabelZoom\Sdk\Internal\ConversionDraft;

/**
 * Cuts each page of an image or PDF source into a grid of labels, read left to right and then
 * down. Configured inside `withSplit(fn (SplitOptions $s) => ...)`.
 *
 * Applies to image or PDF to ZPL in IMAGE mode. Every length is in **inches** on the page as a
 * viewer displays it, measured from its top-left corner.
 */
final class SplitOptions
{
    /** @internal */
    public function __construct(private readonly ConversionDraft $draft)
    {
    }

    /** Rows of labels on each page. The server default is 1. */
    public function rows(int $rows): self
    {
        $this->draft->nested('split', 'rows', $rows);

        return $this;
    }

    /** Columns of labels on each page. The server default is 1. */
    public function columns(int $columns): self
    {
        $this->draft->nested('split', 'columns', $columns);

        return $this;
    }

    /** The size of one label on the page, **in inches**. Omit this to divide the page equally. */
    public function labelSize(float $widthInches, float $heightInches): self
    {
        $this->draft->nested('split', 'width', $widthInches);
        $this->draft->nested('split', 'height', $heightInches);

        return $this;
    }

    /** Distance from the page's left and top edges to the first label, **in inches**. */
    public function margins(float $leftInches, float $topInches): self
    {
        $this->draft->nested('split', 'left', $leftInches);
        $this->draft->nested('split', 'top', $topInches);

        return $this;
    }

    /** Space between two columns and between two rows of labels, **in inches**. */
    public function gaps(float $columnGapInches, float $rowGapInches): self
    {
        $this->draft->nested('split', 'columnGap', $columnGapInches);
        $this->draft->nested('split', 'rowGap', $rowGapInches);

        return $this;
    }

    /** Leaves out labels with nothing printable, such as the unused end of a partly filled sheet. */
    public function skipBlank(bool $skipBlank = true): self
    {
        $this->draft->nested('split', 'skipBlank', $skipBlank);

        return $this;
    }
}
