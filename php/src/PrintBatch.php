<?php

declare(strict_types=1);

namespace LabelZoom\Sdk;

use LabelZoom\Sdk\Internal\JsonReader;

/**
 * The jobs a template print created: one per record. Returned by
 * {@see TemplatePrinterTargetBuilder::execute()}, whatever the number of records.
 */
final class PrintBatch
{
    /**
     * @param list<PrintJob> $jobs
     */
    public function __construct(
        private readonly array $jobs,
        private readonly int $remaining = 0,
        private readonly ?string $message = null,
    ) {
    }

    /**
     * @param array<mixed> $json
     *
     * @internal
     */
    public static function fromJson(array $json): self
    {
        // One record comes back as a bare job and several as {"jobs": [...]}. Callers get one shape.
        $jobs = $json['jobs'] ?? null;
        if (!is_array($jobs) || !array_is_list($jobs)) {
            return new self([PrintJob::fromJson($json)]);
        }

        $list = [];
        foreach ($jobs as $job) {
            $list[] = PrintJob::fromJson(is_array($job) ? $job : []);
        }

        return new self(
            $list,
            JsonReader::int($json, 'remaining') ?? 0,
            JsonReader::string($json, 'message'),
        );
    }

    /**
     * The jobs submitted, in record order.
     *
     * @return list<PrintJob>
     */
    public function getJobs(): array
    {
        return $this->jobs;
    }

    /**
     * How many records at the end of the batch weren't submitted because the request ran out of
     * time. Send the same request with the same Idempotency-Key to submit them: the records already
     * submitted replay without printing again.
     */
    public function getRemaining(): int
    {
        return $this->remaining;
    }

    /** Whether every record was submitted. */
    public function isComplete(): bool
    {
        return $this->remaining === 0;
    }

    /** The server's explanation when {@see self::getRemaining()} isn't 0; otherwise null. */
    public function getMessage(): ?string
    {
        return $this->message;
    }
}
