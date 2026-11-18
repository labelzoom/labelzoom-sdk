package com.labelzoom.sdk;

import java.util.UUID;

/**
 * The print options' validation and wire spelling, written once.
 *
 * <p>The printer target stages call into these the way every stage calls into
 * {@link ParameterWriters}, so a document print and a template print can't validate {@code copies}
 * differently. Source options and {@code rotation} go through {@link ParameterWriters} itself.
 */
final class PrintWriters {

    static final String JSON_MEDIA_TYPE = "application/json";

    static final int MIN_COPIES = 1;

    static final int MAX_COPIES = 9999;

    private PrintWriters() {
    }

    /**
     * Every source prints except a URL and base64 text (contract P2). Print doesn't fetch, and it
     * reads the body as the format's own bytes.
     */
    static ConversionDraft requirePrintable(ConversionDraft draft) {
        if (draft.source == SourceFormat.URL) {
            throw new LabelZoomValidationException(
                    "source", "A URL can't be printed: print doesn't fetch. Fetch the document and print "
                            + "its bytes.");
        }
        if (draft.base64Text) {
            throw new LabelZoomValidationException(
                    "source", "Base64 text can't be printed: print reads the document's own bytes. Decode "
                            + "it first.");
        }
        return draft;
    }

    static String requireId(String id, String parameter) {
        if (id == null || Compat.isBlank(id)) {
            throw new LabelZoomValidationException(parameter, parameter + " cannot be null or blank.");
        }
        return id;
    }

    static int copies(int copies) {
        if (copies < MIN_COPIES || copies > MAX_COPIES) {
            throw new LabelZoomValidationException(
                    "copies", "copies must be from " + MIN_COPIES + " to " + MAX_COPIES + ", but was "
                            + copies + ".");
        }
        return copies;
    }

    static String idempotencyKey(String key) {
        if (key == null || Compat.isBlank(key)) {
            throw new LabelZoomValidationException(
                    "idempotencyKey",
                    "An Idempotency-Key cannot be null or blank. Omit withIdempotencyKey to have the SDK "
                            + "generate one.");
        }
        return key;
    }

    /**
     * The key for one call: the caller's, or a new random UUID. It is fixed before the first attempt,
     * so every retry of the call sends the same one (contract P4).
     */
    static String keyForCall(String callerKey) {
        return callerKey != null ? callerKey : UUID.randomUUID().toString();
    }

    /** Percent-encodes an id as one path segment, so a {@code /} in it can't change the route. */
    static String segment(String id) {
        return LabelZoomClient.encode(id);
    }
}
