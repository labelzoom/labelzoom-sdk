<?php

declare(strict_types=1);

namespace LabelZoom\Sdk;

use LabelZoom\Sdk\Internal\ConversionDraft;

/**
 * The target stage for PDF. A PDF has no resolution of its own, so there is no `withDpi()`.
 */
final class PdfTargetBuilder extends TargetStage
{
    /** @internal */
    public function __construct(ConversionDraft $draft)
    {
        parent::__construct($draft, TargetFormat::Pdf);
    }
}
