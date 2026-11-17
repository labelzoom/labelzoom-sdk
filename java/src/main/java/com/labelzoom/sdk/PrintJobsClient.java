package com.labelzoom.sdk;

/** Print jobs, from {@link LabelZoomClient#jobs()}. */
public final class PrintJobsClient {

    private final LabelZoomClient client;

    PrintJobsClient(LabelZoomClient client) {
        this.client = client;
    }

    /**
     * Gets a job's current state. Poll it to follow a job from {@code queued} or {@code dispatched}
     * to {@code completed} or {@code failed}. Job history is kept for 30 days.
     *
     * @param jobId the {@link PrintJob#jobId()} a print returned
     * @throws LabelZoomValidationException if {@code jobId} is null or blank
     * @throws LabelZoomException if the API returns a non-2xx response; a
     *     {@link LabelZoomNotFoundException} when there is no such job
     */
    public PrintJobDetails get(String jobId) {
        String id = PrintWriters.requireId(jobId, "jobId");
        HttpTransport.Request request = client.request(
                "GET", client.apiUri("/api/v3/jobs/" + PrintWriters.segment(id)),
                PrintWriters.JSON_MEDIA_TYPE, null, null, null);
        return client.send(request, response -> PrintJobDetails.fromJson(PrintResponses.read(response)));
    }
}
