package com.labelzoom.sdk;

/**
 * A request was rejected locally, before any network call.
 *
 * <p>Deliberately <em>not</em> a {@link LabelZoomException}: this means the calling code is wrong,
 * not that the server refused something. It carries no status code, it is never retried, and a
 * caller catching {@code LabelZoomException} to implement fallback behaviour should not swallow it.
 */
public final class LabelZoomValidationException extends IllegalArgumentException {

    private static final long serialVersionUID = 1L;

    private final String parameter;

    LabelZoomValidationException(String parameter, String message) {
        super(message);
        this.parameter = parameter;
    }

    /** The conversion parameter at fault, named as it appears on the wire. */
    public String parameter() {
        return parameter;
    }
}
