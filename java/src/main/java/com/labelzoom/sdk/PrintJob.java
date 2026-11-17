package com.labelzoom.sdk;

import java.util.Map;
import java.util.OptionalInt;

/** A print job the server accepted. */
public final class PrintJob {

    private final String jobId;
    private final String status;
    private final Integer copies;
    private final boolean idempotent;

    PrintJob(String jobId, String status, Integer copies, boolean idempotent) {
        this.jobId = jobId;
        this.status = status;
        this.copies = copies;
        this.idempotent = idempotent;
    }

    static PrintJob fromJson(Map<String, Object> json) {
        String jobId = PrintResponses.string(json, "jobId");
        String status = PrintResponses.string(json, "status");
        return new PrintJob(
                jobId == null ? "" : jobId,
                status == null ? "" : status,
                PrintResponses.integer(json, "copies"),
                PrintResponses.bool(json, "idempotent"));
    }

    /** The job's id. Pass it to {@link PrintJobsClient#get(String)} to follow the job. */
    public String jobId() {
        return jobId;
    }

    /**
     * {@code dispatched} when an online agent took the job, or {@code queued} when none is online
     * and it prints when one reconnects. Kept as the server's string, since statuses can be added.
     */
    public String status() {
        return status;
    }

    /** The copies requested, or empty when the request didn't set any. */
    public OptionalInt copies() {
        return copies == null ? OptionalInt.empty() : OptionalInt.of(copies);
    }

    /**
     * True when the request reused an Idempotency-Key: this is the original job, and nothing printed
     * again.
     */
    public boolean isIdempotent() {
        return idempotent;
    }
}
