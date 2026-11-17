<?php

declare(strict_types=1);

namespace LabelZoom\Sdk;

use LabelZoom\Sdk\Internal\ParameterWriters;

/**
 * Prints a ZPL document.
 *
 * The print twin of {@see ZplSourceBuilder}.
 */
final class ZplPrintBuilder extends PrintStage
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

    /**
     * ZPL commands to skip while reading the document, for example `^PQ`.
     *
     * @param array<array-key, string> $commands
     */
    public function withCommandsToIgnore(array $commands): self
    {
        ParameterWriters::zplCommandsToIgnore($this->draft, $commands);

        return $this;
    }
}
