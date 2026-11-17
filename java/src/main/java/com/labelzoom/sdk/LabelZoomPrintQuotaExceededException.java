package com.labelzoom.sdk;

/**
 * HTTP 429 with code {@code daily_job_limit_reached}: the account's daily print job limit is used
 * up. It resets at 00:00 UTC, which {@link #retryAfterSeconds()} points at.
 *
 * <p>A kind of {@link LabelZoomRateLimitedException}, so a handler for that catches this too. Unlike
 * other 429s, it is never retried automatically: the wait is hours, not seconds. Call
 * {@link LabelZoomClient#getPrintQuota()} to check the remaining count before printing.
 */
public final class LabelZoomPrintQuotaExceededException extends LabelZoomRateLimitedException {

    private static final long serialVersionUID = 1L;

    /** The error {@code code} that marks a 429 as the daily print limit. */
    static final String DAILY_JOB_LIMIT_CODE = "daily_job_limit_reached";

    LabelZoomPrintQuotaExceededException(
            String message, String requestId, String rawBody, String code, Integer retryAfterSeconds) {
        super(message, requestId, rawBody, code, retryAfterSeconds);
    }
}
