package com.labelzoom.sdk;

import java.util.Optional;

/**
 * Base type for every error the LabelZoom API returns. Catch this to handle them all.
 *
 * <p>Extends {@link RuntimeException} on purpose: a checked exception on every {@code execute()} is
 * hostile inside a fluent chain, and the overwhelmingly common handling is to let it propagate.
 *
 * <p>Note that {@link LabelZoomValidationException} deliberately does <em>not</em> extend this.
 */
public class LabelZoomException extends RuntimeException {

    private static final long serialVersionUID = 1L;

    private final int statusCode;
    private final String requestId;
    private final String rawBody;
    private final String code;

    LabelZoomException(int statusCode, String message, String requestId, String rawBody, String code) {
        super(message);
        this.statusCode = statusCode;
        this.requestId = requestId;
        this.rawBody = rawBody;
        this.code = code;
    }

    /** The HTTP status code the API returned. */
    public int statusCode() {
        return statusCode;
    }

    /**
     * The {@code X-LZ-Request-Id} response header, if the server sent one. Quote it to LabelZoom
     * support — it identifies the exact request server-side.
     */
    public Optional<String> requestId() {
        return Optional.ofNullable(requestId);
    }

    /**
     * The raw response body, untruncated. {@link #getMessage()} is derived from it and capped at
     * 512 characters; this is not.
     */
    public Optional<String> rawBody() {
        return Optional.ofNullable(rawBody);
    }

    /**
     * The error body's top-level {@code code}, such as {@code agent_update_required}, or empty when
     * the body has none. Codes are stable, so branch on this rather than on {@link #getMessage()}.
     */
    public Optional<String> code() {
        return Optional.ofNullable(code);
    }
}
