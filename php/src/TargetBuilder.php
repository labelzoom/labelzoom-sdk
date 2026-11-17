<?php

declare(strict_types=1);

namespace LabelZoom\Sdk;

use LabelZoom\Sdk\Internal\ConversionDraft;
use LabelZoom\Sdk\Internal\ParameterWriters;

/**
 * The target stage for EPL, IPL, TSPL, DPL, SBPL, LabelZoom XML and LabelZoom JSON.
 */
final class TargetBuilder extends TargetStage
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
}
