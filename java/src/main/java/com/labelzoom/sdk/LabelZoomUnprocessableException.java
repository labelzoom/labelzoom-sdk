package com.labelzoom.sdk;

/**
 * HTTP 422. The request was well formed but can't be carried out. A print returns it with a
 * {@link #code()}: {@code agent_update_required} when the print agent serving the printer is too old
 * to print copies, or {@code idempotency_key_reused} when the key was used for a different request.
 * A template print with {@code withValidate(true)} returns it for missing merge fields.
 */
public final class LabelZoomUnprocessableException extends LabelZoomException {

    private static final long serialVersionUID = 1L;

    LabelZoomUnprocessableException(String message, String requestId, String rawBody, String code) {
        super(422, message, requestId, rawBody, code);
    }
}
