<?php

declare(strict_types=1);

namespace LabelZoom\Sdk;

use LabelZoom\Sdk\Internal\JsonReader;

/**
 * A print job's current state. Returned by {@see PrintJobsClient::get()}.
 */
final class PrintJobDetails
{
    public function __construct(
        private readonly string $id,
        private readonly string $status,
        private readonly ?string $printerId = null,
        private readonly ?string $agentId = null,
        private readonly ?string $sourceFormat = null,
        private readonly ?int $copies = null,
        private readonly ?string $error = null,
        private readonly int $retryCount = 0,
        private readonly ?\DateTimeImmutable $createdDate = null,
        private readonly ?\DateTimeImmutable $dispatchedDate = null,
        private readonly ?\DateTimeImmutable $completedDate = null,
        private readonly ?\DateTimeImmutable $retriedDate = null,
    ) {
    }

    /**
     * Reads the snake_case wire shape (rule P6).
     *
     * @param array<mixed> $json
     *
     * @internal
     */
    public static function fromJson(array $json): self
    {
        return new self(
            id: JsonReader::string($json, 'id') ?? '',
            status: JsonReader::string($json, 'status') ?? '',
            printerId: JsonReader::string($json, 'printer_id'),
            agentId: JsonReader::string($json, 'agent_id'),
            sourceFormat: JsonReader::string($json, 'source_format'),
            copies: JsonReader::int($json, 'copies'),
            error: JsonReader::string($json, 'error'),
            retryCount: JsonReader::int($json, 'retry_count') ?? 0,
            createdDate: JsonReader::instant($json, 'created_date'),
            dispatchedDate: JsonReader::instant($json, 'dispatched_date'),
            completedDate: JsonReader::instant($json, 'completed_date'),
            retriedDate: JsonReader::instant($json, 'retried_date'),
        );
    }

    /** The job's id. */
    public function getId(): string
    {
        return $this->id;
    }

    /** The printer the job is for. */
    public function getPrinterId(): ?string
    {
        return $this->printerId;
    }

    /** The agent that took the job, or null while it is queued. */
    public function getAgentId(): ?string
    {
        return $this->agentId;
    }

    /** The format the job was stored in: the printer's language after any conversion. */
    public function getSourceFormat(): ?string
    {
        return $this->sourceFormat;
    }

    /** The copies requested, or null when the request didn't set any. */
    public function getCopies(): ?int
    {
        return $this->copies;
    }

    /**
     * `queued`, `dispatched`, `printing`, `completed` or `failed`. Kept as the server's string, since
     * statuses can be added.
     */
    public function getStatus(): string
    {
        return $this->status;
    }

    /** Why the job failed, or null. */
    public function getError(): ?string
    {
        return $this->error;
    }

    /** How many times the job was retried. */
    public function getRetryCount(): int
    {
        return $this->retryCount;
    }

    /** When the job was created. */
    public function getCreatedDate(): ?\DateTimeImmutable
    {
        return $this->createdDate;
    }

    /** When an agent took the job, or null. */
    public function getDispatchedDate(): ?\DateTimeImmutable
    {
        return $this->dispatchedDate;
    }

    /** When the job completed or failed, or null. */
    public function getCompletedDate(): ?\DateTimeImmutable
    {
        return $this->completedDate;
    }

    /** When the job was last retried, or null. */
    public function getRetriedDate(): ?\DateTimeImmutable
    {
        return $this->retriedDate;
    }
}
