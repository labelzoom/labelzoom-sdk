<?php

declare(strict_types=1);

namespace LabelZoom\Sdk;

use LabelZoom\Sdk\Internal\JsonReader;

/**
 * One counted quota: how much is used, the limit, and what remains.
 */
final class PrintQuotaCounter
{
    public function __construct(
        private readonly int $used,
        private readonly int $limit,
        private readonly int $remaining,
        private readonly ?int $pending = null,
        private readonly ?\DateTimeImmutable $resetAt = null,
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
            JsonReader::int($json, 'used') ?? 0,
            JsonReader::int($json, 'limit') ?? 0,
            JsonReader::int($json, 'remaining') ?? 0,
            JsonReader::int($json, 'pending'),
            JsonReader::instant($json, 'resetAt'),
        );
    }

    /** How many are used. */
    public function getUsed(): int
    {
        return $this->used;
    }

    /** The plan's limit. */
    public function getLimit(): int
    {
        return $this->limit;
    }

    /** How many remain. */
    public function getRemaining(): int
    {
        return $this->remaining;
    }

    /** For agents only: unused enrollment codes, which hold a slot until they expire. */
    public function getPending(): ?int
    {
        return $this->pending;
    }

    /** For jobs today only: when the count resets. */
    public function getResetAt(): ?\DateTimeImmutable
    {
        return $this->resetAt;
    }
}
