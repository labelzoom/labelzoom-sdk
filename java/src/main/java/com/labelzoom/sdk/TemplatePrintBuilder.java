package com.labelzoom.sdk;

import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;

/**
 * Prints a published template, one label per record. Returned by
 * {@link PrintRequestBuilder#fromTemplate(String)}.
 *
 * <p>A template is already a label, so there are no source options. Each label is converted to the
 * printer's language.
 */
public final class TemplatePrintBuilder {

    private static final String NO_RECORDS =
            "Provide at least one record, or omit withData for a template with no merge fields.";

    private final LabelZoomClient client;
    private final String printerId;
    private final String templateId;
    private List<Object> records = new ArrayList<>();
    private boolean validate;
    private Integer copies;
    private String idempotencyKey;

    TemplatePrintBuilder(LabelZoomClient client, String printerId, String templateId) {
        this.client = client;
        this.printerId = printerId;
        this.templateId = templateId;
    }

    /**
     * The merge-field values: one map per label, whose keys are the template's merge fields. A
     * template with no merge fields needs none. A batch is limited by the plan's
     * {@link PrintQuotaLimits#maxBatchSize()}.
     */
    @SafeVarargs
    public final TemplatePrintBuilder withData(Map<String, ?>... records) {
        if (records == null || records.length == 0) {
            throw new LabelZoomValidationException(
                    "data", NO_RECORDS);
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
    public TemplatePrintBuilder withData(List<? extends Map<String, ?>> records) {
        if (records == null || records.isEmpty()) {
            throw new LabelZoomValidationException(
                    "data", NO_RECORDS);
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
    public TemplatePrintBuilder withValidate(boolean validate) {
        this.validate = validate;
        return this;
    }

    /** Sends each label to the printer this many times, from 1 to 9999. */
    public TemplatePrintBuilder withCopies(int copies) {
        this.copies = PrintWriters.copies(copies);
        return this;
    }

    /**
     * Makes the print safe to repeat. In a batch the server gives each record its own key, derived
     * from this one, so a repeated batch prints only the records that didn't print before.
     */
    public TemplatePrintBuilder withIdempotencyKey(String key) {
        this.idempotencyKey = PrintWriters.idempotencyKey(key);
        return this;
    }

    /**
     * Sends the labels to the printer.
     *
     * @return the jobs created. If {@link PrintBatch#remaining()} isn't 0, send the same request
     *     again with the same Idempotency-Key to submit the rest.
     * @throws LabelZoomException if the API returns a non-2xx response
     */
    public PrintBatch send() {
        StringBuilder query = new StringBuilder();
        if (copies != null) {
            query.append("copies=").append(copies);
        }
        if (validate) {
            query.append(query.length() > 0 ? "&" : "").append("validate=true");
        }

        String path = "/api/v3/printers/" + PrintWriters.segment(printerId)
                + "/templates/" + PrintWriters.segment(templateId) + "/print";
        String uri = client.apiUri(query.length() == 0 ? path : path + "?" + query);
        byte[] body = body(records).getBytes(StandardCharsets.UTF_8);

        HttpTransport.Request request = client.request(
                "POST", uri, PrintWriters.JSON_MEDIA_TYPE, body, PrintWriters.JSON_MEDIA_TYPE,
                PrintWriters.keyForCall(idempotencyKey));
        return client.send(request, response -> PrintBatch.fromJson(PrintResponses.read(response)));
    }

    /** One record is sent as an object, several as an array, and none as {@code {}} (contract P3). */
    private static String body(List<Object> records) {
        try {
            return switch (records.size()) {
                case 0 -> "{}";
                case 1 -> Json.write(records.get(0));
                default -> Json.write(records);
            };
        } catch (LabelZoomValidationException e) {
            throw new LabelZoomValidationException("data", e.getMessage());
        }
    }
}
