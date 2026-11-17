package com.labelzoom.sdk;

import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.Map;
import java.util.Optional;

/** The jobs a template print created: one per record. */
public final class PrintBatch {

    private final List<PrintJob> jobs;
    private final int remaining;
    private final String message;

    PrintBatch(List<PrintJob> jobs, int remaining, String message) {
        this.jobs = Collections.unmodifiableList(jobs);
        this.remaining = remaining;
        this.message = message;
    }

    @SuppressWarnings("unchecked")
    static PrintBatch fromJson(Map<String, Object> json) {
        // One record comes back as a bare job, several as {"jobs": [...]}. Callers get one shape.
        if (!(json.get("jobs") instanceof List<?> entries)) {
            return new PrintBatch(List.of(PrintJob.fromJson(json)), 0, null);
        }
        List<PrintJob> jobs = new ArrayList<>(entries.size());
        for (Object entry : entries) {
            if (entry instanceof Map) {
                jobs.add(PrintJob.fromJson((Map<String, Object>) entry));
            }
        }
        Integer remaining = PrintResponses.integer(json, "remaining");
        return new PrintBatch(
                jobs, remaining == null ? 0 : remaining, PrintResponses.string(json, "message"));
    }

    /** The jobs submitted, in record order. */
    public List<PrintJob> jobs() {
        return jobs;
    }

    /**
     * How many records at the end of the batch weren't submitted because the request ran out of
     * time. Send the same request with the same Idempotency-Key to submit them: the records already
     * submitted replay without printing again.
     */
    public int remaining() {
        return remaining;
    }

    /** Whether every record was submitted. */
    public boolean isComplete() {
        return remaining == 0;
    }

    /** The server's explanation when {@link #remaining()} isn't 0; otherwise empty. */
    public Optional<String> message() {
        return Optional.ofNullable(message);
    }
}
