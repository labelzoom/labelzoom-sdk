<?php

declare(strict_types=1);

namespace LabelZoom\Sdk;

use LabelZoom\Sdk\Internal\JsonReader;

/**
 * A print job the server accepted. Returned by {@see PrinterTargetBuilder::execute()}, and one per
 * record in a {@see PrintBatch}.
 */
final class PrintJob
{
    public function __construct(
        private readonly string $jobId,
        private readonly string $status,
        private readonly ?int $copies = null,
        private readonly bool $idempotent = false,
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
            JsonReader::string($json, 'jobId') ?? '',
            JsonReader::string($json, 'status') ?? '',
            JsonReader::int($json, 'copies'),
            // A response that omits it is a new job, not a replay (rule P6).
            JsonReader::isTrue($json, 'idempotent'),
        );
    }

    /** The job's id. Pass it to {@see PrintJobsClient::get()} to follow the job. */
    public function getJobId(): string
    {
        return $this->jobId;
    }

    /**
     * `dispatched` when an online agent took the job, or `queued` when none is online and it prints
     * when one reconnects. Kept as the server's string, since statuses can be added.
     */
    public function getStatus(): string
    {
        return $this->status;
    }

    /** The copies requested, or null when the request didn't set any. */
    public function getCopies(): ?int
    {
        return $this->copies;
    }

    /**
     * True when the request reused an Idempotency-Key: this is the original job, and nothing
     * printed again.
     */
    public function isIdempotent(): bool
    {
        return $this->idempotent;
    }
}
