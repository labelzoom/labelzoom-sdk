package com.labelzoom.sdk;

/** HTTP 400. The request was malformed or the conversion path is invalid. */
public final class LabelZoomBadRequestException extends LabelZoomException {

    private static final long serialVersionUID = 1L;

    LabelZoomBadRequestException(String message, String requestId, String rawBody) {
        super(400, message, requestId, rawBody);
    }
}
