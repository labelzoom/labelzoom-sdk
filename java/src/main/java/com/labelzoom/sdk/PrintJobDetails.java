package com.labelzoom.sdk;

import java.time.Instant;
import java.util.Map;
import java.util.Optional;
import java.util.OptionalInt;

/** A print job's current state, from {@link PrintJobsClient#get(String)}. */
public final class PrintJobDetails {

    private final String id;
    private final String printerId;
    private final String agentId;
    private final String sourceFormat;
    private final Integer copies;
    private final String status;
    private final String error;
    private final int retryCount;
    private final Instant createdDate;
    private final Instant dispatchedDate;
    private final Instant completedDate;
    private final Instant retriedDate;

    private PrintJobDetails(Map<String, Object> json) {
        String jobId = PrintResponses.string(json, "id");
        String jobStatus = PrintResponses.string(json, "status");
        Integer retries = PrintResponses.integer(json, "retry_count");
        this.id = jobId == null ? "" : jobId;
        this.printerId = PrintResponses.string(json, "printer_id");
        this.agentId = PrintResponses.string(json, "agent_id");
        this.sourceFormat = PrintResponses.string(json, "source_format");
        this.copies = PrintResponses.integer(json, "copies");
        this.status = jobStatus == null ? "" : jobStatus;
        this.error = PrintResponses.string(json, "error");
        this.retryCount = retries == null ? 0 : retries;
        this.createdDate = PrintResponses.instant(json, "created_date");
        this.dispatchedDate = PrintResponses.instant(json, "dispatched_date");
        this.completedDate = PrintResponses.instant(json, "completed_date");
        this.retriedDate = PrintResponses.instant(json, "retried_date");
    }

    static PrintJobDetails fromJson(Map<String, Object> json) {
        return new PrintJobDetails(json);
    }

    /** The job's id. */
    public String id() {
        return id;
    }

    /** The printer the job is for. */
    public Optional<String> printerId() {
        return Optional.ofNullable(printerId);
    }

    /** The agent that took the job, or empty while it is queued. */
    public Optional<String> agentId() {
        return Optional.ofNullable(agentId);
    }

    /** The format the job was stored in: the printer's language after any conversion. */
    public Optional<String> sourceFormat() {
        return Optional.ofNullable(sourceFormat);
    }

    /** The copies requested, or empty when the request didn't set any. */
    public OptionalInt copies() {
        return copies == null ? OptionalInt.empty() : OptionalInt.of(copies);
    }

    /**
     * {@code queued}, {@code dispatched}, {@code printing}, {@code completed} or {@code failed}. Kept
     * as the server's string, since statuses can be added.
     */
    public String status() {
        return status;
    }

    /** Why the job failed, or empty. */
    public Optional<String> error() {
        return Optional.ofNullable(error);
    }

    /** How many times the job was retried from the dashboard. */
    public int retryCount() {
        return retryCount;
    }

    /** When the job was created. */
    public Optional<Instant> createdDate() {
        return Optional.ofNullable(createdDate);
    }

    /** When an agent took the job, or empty. */
    public Optional<Instant> dispatchedDate() {
        return Optional.ofNullable(dispatchedDate);
    }

    /** When the job completed or failed, or empty. */
    public Optional<Instant> completedDate() {
        return Optional.ofNullable(completedDate);
    }

    /** When the job was last retried, or empty. */
    public Optional<Instant> retriedDate() {
        return Optional.ofNullable(retriedDate);
    }
}
