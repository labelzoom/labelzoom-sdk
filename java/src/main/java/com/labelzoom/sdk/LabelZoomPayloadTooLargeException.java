package com.labelzoom.sdk;

/** HTTP 413. The body exceeded the tier's limit — 1 MB on the anonymous free tier. */
public final class LabelZoomPayloadTooLargeException extends LabelZoomException {

    private static final long serialVersionUID = 1L;

    LabelZoomPayloadTooLargeException(String message, String requestId, String rawBody) {
        super(413, message, requestId, rawBody);
    }
}
