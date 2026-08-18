package com.labelzoom.sdk;

/** HTTP 401. The supplied credential was rejected. */
public final class LabelZoomUnauthorizedException extends LabelZoomException {

    private static final long serialVersionUID = 1L;

    LabelZoomUnauthorizedException(String message, String requestId, String rawBody) {
        super(401, message, requestId, rawBody);
    }
}
