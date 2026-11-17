package com.labelzoom.sdk;

/**
 * A document print under construction.
 *
 * <p>The document and its source options are a {@link ConversionDraft}, so the print stages write
 * them through the same {@link ParameterWriters} calls as the convert source stages, and they
 * serialize to the same {@code params} object.
 */
final class PrintDraft {

    final ConversionDraft document;
    final String printerId;
    Integer copies;
    String idempotencyKey;

    PrintDraft(ConversionDraft document, String printerId) {
        this.document = document;
        this.printerId = printerId;
    }
}
