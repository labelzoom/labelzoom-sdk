<?php

declare(strict_types=1);

namespace LabelZoom\Sdk;

use LabelZoom\Sdk\Internal\ParameterWriters;

/**
 * The source stage for a ZPL document.
 */
final class ZplSourceBuilder extends SourceStage
{
    /**
     * The resolution the source was made for, in dots per inch: how its dots or pixels map to
     * inches. The server default is 203. The output follows it unless the target stage sets its own.
     */
    public function withDpi(int $dpi): self
    {
        ParameterWriters::sourceDpi($this->draft, $dpi);

        return $this;
    }

    /**
     * ZPL commands the parser should skip, for example `^PQ`.
     *
     * @param array<array-key, string> $commands
     */
    public function withCommandsToIgnore(array $commands): self
    {
        ParameterWriters::zplCommandsToIgnore($this->draft, $commands);

        return $this;
    }
}
