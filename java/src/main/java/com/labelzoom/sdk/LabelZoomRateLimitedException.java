package com.labelzoom.sdk;

import java.util.OptionalInt;

/** HTTP 429. Too many requests. */
public final class LabelZoomRateLimitedException extends LabelZoomException {

    private static final long serialVersionUID = 1L;

    private final Integer retryAfterSeconds;

    LabelZoomRateLimitedException(
            String message, String requestId, String rawBody, Integer retryAfterSeconds) {
        super(429, message, requestId, rawBody);
        this.retryAfterSeconds = retryAfterSeconds;
    }

    /**
     * {@code Retry-After} in seconds, when the server sent one. The SDK already honours this during
     * its own retries; this exposes it for callers doing their own.
     */
    public OptionalInt retryAfterSeconds() {
        return retryAfterSeconds == null ? OptionalInt.empty() : OptionalInt.of(retryAfterSeconds);
    }
}
