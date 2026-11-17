package com.labelzoom.sdk;

import java.util.EnumSet;
import java.util.Set;
import java.util.UUID;

/**
 * The print options' validation and wire spelling, written once.
 *
 * <p>The print stages call into these the way every stage calls into {@link ParameterWriters}, so a
 * document print and a template print can't validate {@code copies} differently. Source options go
 * through {@link ParameterWriters} itself.
 */
final class PrintWriters {

    static final String JSON_MEDIA_TYPE = "application/json";

    static final int MIN_COPIES = 1;

    static final int MAX_COPIES = 9999;

    /**
     * The sources the print endpoint converts to the printer's language (contract P2). It sends any
     * other format to the printer as is, so the SDK refuses it.
     */
    private static final Set<SourceFormat> PRINTABLE = EnumSet.of(
            SourceFormat.ZPL, SourceFormat.EPL, SourceFormat.IPL, SourceFormat.TSPL,
            SourceFormat.DPL, SourceFormat.SBPL, SourceFormat.FINGERPRINT, SourceFormat.CPCL,
            SourceFormat.XML, SourceFormat.JSON, SourceFormat.PDF, SourceFormat.PNG,
            SourceFormat.BMP, SourceFormat.GIF, SourceFormat.JPEG, SourceFormat.JPG);

    private PrintWriters() {
    }

    static SourceFormat requirePrintable(SourceFormat format) {
        if (format == null) {
            throw new LabelZoomValidationException("source", "Source format cannot be null.");
        }
        if (!PRINTABLE.contains(format)) {
            throw new LabelZoomValidationException(
                    "source",
                    format.wireToken() + " can't be printed directly: print converts only ZPL, EPL, IPL, "
                            + "TSPL, DPL, SBPL, Fingerprint, CPCL, LabelZoom XML and JSON, PDF and images. "
                            + "Convert it to the printer's language with convert() first, then print the "
                            + "result.");
        }
        return format;
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
