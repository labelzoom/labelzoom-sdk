package com.labelzoom.sdk;

import java.nio.charset.StandardCharsets;
import java.util.List;

/**
 * The printer target stage for a template, after {@link TemplateSourceBuilder#toPrinter(String)}.
 *
 * <p>A template takes no target option: each label is converted to the printer's language.
 */
public final class TemplatePrinterTargetBuilder {

    private final LabelZoomClient client;
    private final String templateId;
    private final String printerId;
    private final List<Object> records;
    private final boolean validate;
    private Integer copies;
    private String idempotencyKey;

    TemplatePrinterTargetBuilder(
            LabelZoomClient client, String templateId, String printerId, List<Object> records,
            boolean validate) {
        this.client = client;
        this.templateId = templateId;
        this.printerId = printerId;
        this.records = records;
        this.validate = validate;
    }

    /** Sends each label to the printer this many times, from 1 to 9999. */
    public TemplatePrinterTargetBuilder withCopies(int copies) {
        this.copies = PrintWriters.copies(copies);
        return this;
    }

    /**
     * Makes the print safe to repeat. In a batch the server gives each record its own key, derived
     * from this one, so a repeated batch prints only the records that didn't print before.
     */
    public TemplatePrinterTargetBuilder withIdempotencyKey(String key) {
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
    public PrintBatch execute() {
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
