package com.labelzoom.sdk;

/** HTTP 403. The credential is valid but not entitled to this operation. */
public final class LabelZoomForbiddenException extends LabelZoomException {

    private static final long serialVersionUID = 1L;

    private final boolean paidFeature;

    LabelZoomForbiddenException(String message, String requestId, String rawBody, boolean paidFeature) {
        super(403, message, requestId, rawBody);
        this.paidFeature = paidFeature;
    }

    /**
     * True when this 403 is a paywall rather than a permissions problem — "JSON export is a paid
     * feature" and friends.
     *
     * <p>By far the most common anonymous-tier failure, so it gets a flag instead of leaving
     * callers to match strings.
     */
    public boolean isPaidFeature() {
        return paidFeature;
    }
}
