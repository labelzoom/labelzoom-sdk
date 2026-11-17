<?php

declare(strict_types=1);

namespace LabelZoom\Sdk;

use LabelZoom\Sdk\Internal\ParameterWriters;

/**
 * Prints EPL, IPL, TSPL, DPL, SBPL, Fingerprint or CPCL.
 *
 * The print twin of {@see PrinterLanguageSourceBuilder}.
 */
final class PrinterLanguagePrintBuilder extends PrintStage
{
    /**
     * The resolution the source was made for, in dots per inch: how its dots or pixels map to
     * inches. The server default is 203. Set it when the document was made for a 300 or 600 dpi
     * head.
     */
    public function withDpi(int $dpi): self
    {
        ParameterWriters::sourceDpi($this->draft, $dpi);

        return $this;
    }
}
