<?php

declare(strict_types=1);

namespace LabelZoom\Sdk;

use LabelZoom\Sdk\Internal\JsonReader;

/**
 * The account's Cloud Print limits and usage. Returned by `LabelZoomClient::getPrintQuota()`.
 */
final class PrintQuota
{
    public function __construct(
        private readonly string $plan,
        private readonly PrintQuotaCounter $agents,
        private readonly PrintQuotaCounter $printers,
        private readonly PrintQuotaCounter $jobsToday,
        private readonly PrintQuotaLimits $limits,
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
            JsonReader::string($json, 'plan') ?? '',
            PrintQuotaCounter::fromJson(JsonReader::object($json, 'agents')),
            PrintQuotaCounter::fromJson(JsonReader::object($json, 'printers')),
            PrintQuotaCounter::fromJson(JsonReader::object($json, 'jobsToday')),
            PrintQuotaLimits::fromJson(JsonReader::object($json, 'limits')),
        );
    }

    /** The account's print plan. */
    public function getPlan(): string
    {
        return $this->plan;
    }

    /** Enrolled agents. Unused enrollment codes count in {@see PrintQuotaCounter::getPending()}. */
    public function getAgents(): PrintQuotaCounter
    {
        return $this->agents;
    }

    /** Printers added. */
    public function getPrinters(): PrintQuotaCounter
    {
        return $this->printers;
    }

    /**
     * Jobs created today. The count resets at {@see PrintQuotaCounter::getResetAt()}, 00:00 UTC;
     * past the limit, a print fails with an {@see Exception\PrintQuotaExceededException}.
     */
    public function getJobsToday(): PrintQuotaCounter
    {
        return $this->jobsToday;
    }

    /** The size limits for a print request. */
    public function getLimits(): PrintQuotaLimits
    {
        return $this->limits;
    }
}
