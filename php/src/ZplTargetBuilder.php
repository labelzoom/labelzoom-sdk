<?php

declare(strict_types=1);

namespace LabelZoom\Sdk;

use LabelZoom\Sdk\Internal\ConversionDraft;
use LabelZoom\Sdk\Internal\ParameterWriters;

/**
 * The target stage for ZPL output.
 */
final class ZplTargetBuilder extends TargetStage
{
    /** @internal */
    public function __construct(ConversionDraft $draft)
    {
        parent::__construct($draft, TargetFormat::Zpl);
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
     * Image compression in the ZPL output. The server default is {@see ZplImageCompression::Z64}.
     */
    public function withImageCompression(ZplImageCompression $compression): self
    {
        ParameterWriters::zplImageCompression($this->draft, $compression);

        return $this;
    }

    /**
     * Image or PDF source: each run of identical labels becomes one label with a `^PQ` count. The
     * server default is `false`.
     */
    public function withCollapseCopies(bool $collapse = true): self
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
}
