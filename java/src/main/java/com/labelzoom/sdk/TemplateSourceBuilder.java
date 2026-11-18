package com.labelzoom.sdk;

import java.util.ArrayList;
import java.util.List;
import java.util.Map;

/**
 * The source stage for a published template, after
 * {@link ConversionRequestBuilder#fromTemplate(String)}.
 *
 * <p>A template is already a label, so it takes merge data instead of source options. Its only
 * target is a printer.
 */
public final class TemplateSourceBuilder {

    private static final String NO_RECORDS =
            "Provide at least one record, or omit withData for a template with no merge fields.";

    private final LabelZoomClient client;
    private final String templateId;
    private List<Object> records = new ArrayList<>();
    private boolean validate;

    TemplateSourceBuilder(LabelZoomClient client, String templateId) {
        this.client = client;
        this.templateId = templateId;
    }

    /**
     * The merge-field values: one map per label, whose keys are the template's merge fields. A
     * template with no merge fields needs none. A batch is limited by the plan's
     * {@link PrintQuotaLimits#maxBatchSize()}.
     */
    @SafeVarargs
    public final TemplateSourceBuilder withData(Map<String, ?>... records) {
        if (records == null || records.length == 0) {
            throw new LabelZoomValidationException("data", NO_RECORDS);
        }
        // Copied element by element: passing the generic array on to another varargs method would
        // re-raise the heap-pollution warning this method asserts against.
        List<Map<String, ?>> copy = new ArrayList<>(records.length);
        for (Map<String, ?> record : records) {
            copy.add(record);
        }
        return withData(copy);
    }

    /** The merge-field values: one map per label. See {@link #withData(Map[])}. */
    public TemplateSourceBuilder withData(List<? extends Map<String, ?>> records) {
        if (records == null || records.isEmpty()) {
            throw new LabelZoomValidationException("data", NO_RECORDS);
        }
        List<Object> normalized = new ArrayList<>(records.size());
        for (int i = 0; i < records.size(); i++) {
            // Checked at run time as well as by the signature: a list built from decoded JSON, or
            // passed from a language without Java's generics, can hold anything.
            Object record = records.get(i);
            if (!(record instanceof Map)) {
                throw new LabelZoomValidationException(
                        "data", "data[" + i + "] is " + (record == null ? "null" : "not a map")
                                + "; every record must be an object of merge-field values.");
            }
            normalized.add(record);
        }
        this.records = normalized;
        return this;
    }

    /**
     * Checks every record for missing merge fields before anything prints. A missing field fails the
     * whole request with a {@link LabelZoomUnprocessableException}.
     */
    public TemplateSourceBuilder withValidate(boolean validate) {
        this.validate = validate;
        return this;
    }

    /**
     * Prints one label per record on a Cloud Print printer, converted to its language.
     *
     * @param printerId the printer's id, from the Printers page
     * @throws LabelZoomValidationException if {@code printerId} is null or blank
     */
    public TemplatePrinterTargetBuilder toPrinter(String printerId) {
        return new TemplatePrinterTargetBuilder(
                client, templateId, PrintWriters.requireId(printerId, "printerId"),
                new ArrayList<>(records), validate);
    }
}
