<?php

declare(strict_types=1);

namespace LabelZoom\Sdk;

use LabelZoom\Sdk\Internal\JsonReader;

/**
 * The size limits for a print request.
 */
final class PrintQuotaLimits
{
    public function __construct(
        private readonly int $maxPayloadBytes,
        private readonly int $maxStoredPayloadBytes,
        private readonly int $maxBatchSize,
    ) {
    }

    /**
     * @param array<mixed> $json
     *
     * @internal
     */
    public static function fromJson(array $json): self
    {
        return new self(
            JsonReader::int($json, 'maxPayloadBytes') ?? 0,
            JsonReader::int($json, 'maxStoredPayloadBytes') ?? 0,
            JsonReader::int($json, 'maxBatchSize') ?? 0,
        );
    }

    /** The largest document a print request can send, in bytes. */
    public function getMaxPayloadBytes(): int
    {
        return $this->maxPayloadBytes;
    }

    /** The largest document after conversion to the printer's language, in bytes. */
    public function getMaxStoredPayloadBytes(): int
    {
        return $this->maxStoredPayloadBytes;
    }

    /** The most records one template print can send. */
    public function getMaxBatchSize(): int
    {
        return $this->maxBatchSize;
    }
}
