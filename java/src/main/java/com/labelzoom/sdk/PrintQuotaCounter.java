package com.labelzoom.sdk;

import java.time.Instant;
import java.util.Map;
import java.util.Optional;
import java.util.OptionalInt;

/** One counted quota: how much is used, the limit, and what remains. */
public final class PrintQuotaCounter {

    private final int used;
    private final int limit;
    private final int remaining;
    private final Integer pending;
    private final Instant resetAt;

    PrintQuotaCounter(Map<String, Object> json) {
        this.used = orZero(PrintResponses.integer(json, "used"));
        this.limit = orZero(PrintResponses.integer(json, "limit"));
        this.remaining = orZero(PrintResponses.integer(json, "remaining"));
        this.pending = PrintResponses.integer(json, "pending");
        this.resetAt = PrintResponses.instant(json, "resetAt");
    }

    private static int orZero(Integer value) {
        return value == null ? 0 : value;
    }

    /** How many are used. */
    public int used() {
        return used;
    }

    /** The plan's limit. */
    public int limit() {
        return limit;
    }

    /** How many remain. */
    public int remaining() {
        return remaining;
    }

    /** For agents only: unused enrollment codes, which hold a slot until they expire. */
    public OptionalInt pending() {
        return pending == null ? OptionalInt.empty() : OptionalInt.of(pending);
    }

    /** For jobs today only: when the count resets. */
    public Optional<Instant> resetAt() {
        return Optional.ofNullable(resetAt);
    }
}
