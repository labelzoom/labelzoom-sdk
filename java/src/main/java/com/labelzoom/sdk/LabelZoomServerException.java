package com.labelzoom.sdk;

/** HTTP 5xx. Retried automatically before surfacing. */
public final class LabelZoomServerException extends LabelZoomException {

    private static final long serialVersionUID = 1L;

    LabelZoomServerException(int statusCode, String message, String requestId, String rawBody) {
        super(statusCode, message, requestId, rawBody);
    }
}
