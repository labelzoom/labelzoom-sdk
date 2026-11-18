<?php

declare(strict_types=1);

namespace LabelZoom\Sdk;

use LabelZoom\Sdk\Internal\PrintWriters;

/**
 * Print jobs. Returned by `LabelZoomClient::jobs()`.
 */
final class PrintJobsClient
{
    /** @internal */
    public function __construct(private readonly LabelZoomClient $client)
    {
    }

    /**
     * Gets a job's current state. Poll it to follow a job from `queued` or `dispatched` to
     * `completed` or `failed`.
     *
     * @param string $jobId the {@see PrintJob::getJobId()} a print returned
     *
     * @throws Exception\ValidationException if the job id is empty
     * @throws Exception\LabelZoomException if the API returns a non-2xx response
     * @throws Exception\TransportException if no response was ever produced
     */
    public function get(string $jobId): PrintJobDetails
    {
        $id = PrintWriters::requireId($jobId, 'jobId');

        return PrintJobDetails::fromJson(
            $this->client->requestJson('GET', '/api/v3/jobs/' . PrintWriters::segment($id)),
        );
    }
}
