package com.labelzoom.sdk;

/** HTTP 404. The conversion path, printer, template or job does not exist. */
public final class LabelZoomNotFoundException extends LabelZoomException {

    private static final long serialVersionUID = 1L;

    LabelZoomNotFoundException(String message, String requestId, String rawBody, String code) {
        super(404, message, requestId, rawBody, code);
    }
}
