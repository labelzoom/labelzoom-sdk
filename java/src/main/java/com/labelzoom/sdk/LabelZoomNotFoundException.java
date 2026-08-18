package com.labelzoom.sdk;

/** HTTP 404. The conversion path does not exist. */
public final class LabelZoomNotFoundException extends LabelZoomException {

    private static final long serialVersionUID = 1L;

    LabelZoomNotFoundException(String message, String requestId, String rawBody) {
        super(404, message, requestId, rawBody);
    }
}
