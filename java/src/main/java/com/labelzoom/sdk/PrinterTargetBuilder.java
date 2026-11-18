package com.labelzoom.sdk;

import java.util.Map;

/**
 * The printer target stage, after {@link SourceStage#toPrinter(String)}: where the document goes,
 * how many times, and the key that makes the print safe to repeat.
 *
 * <p>The printer's configuration sets the output language, so the only target option is
 * {@link #withRotation(int)}, which turns the finished label whatever the language.
 */
public final class PrinterTargetBuilder {

    private final ConversionDraft draft;
    private final String printerId;
    private Integer copies;
    private String idempotencyKey;

    PrinterTargetBuilder(ConversionDraft draft, String printerId) {
        this.draft = draft;
        this.printerId = printerId;
    }

    /**
     * Rotation in degrees clockwise. Must be a multiple of 90. The server default is 0.
     *
     * @param rotation 0, 90, 180 or 270
     */
    public PrinterTargetBuilder withRotation(int rotation) {
        ParameterWriters.rotation(draft, rotation);
        return this;
    }

    /**
     * Sends the whole job to the printer this many times, from 1 to 9999. It multiplies a quantity
     * in the document: {@code ^PQ3} with 5 copies prints 15 labels.
     */
    public PrinterTargetBuilder withCopies(int copies) {
        this.copies = PrintWriters.copies(copies);
        return this;
    }

    /**
     * Makes the print safe to repeat: a second request with the same key returns the original job
     * instead of printing again. Use something unique to the label, such as an order number.
     *
     * <p>Without it the SDK generates a key for each call to {@link #execute()}, which makes the
     * SDK's own retries safe but not a repeat of the whole call. Keys are scoped to the account; a
     * deliberate reprint needs a new one.
     */
    public PrinterTargetBuilder withIdempotencyKey(String key) {
        this.idempotencyKey = PrintWriters.idempotencyKey(key);
        return this;
    }

    /**
     * Sends the document to the printer. The options are copied when this is called, so a builder
     * can be executed again; each call is a separate print with its own generated key.
     *
     * @return the job the server created, or the original job when the key was used before
     * @throws LabelZoomException if the API returns a non-2xx response
     */
    public PrintJob execute() {
        LabelZoomClient client = draft.client;

        StringBuilder query = new StringBuilder("sourceFormat=")
                .append(LabelZoomClient.encode(draft.source.wireToken()));
        if (copies != null) {
            query.append("&copies=").append(copies);
        }
        // An empty params would make the server convert a document that's already in the
        // printer's language, so it is sent only when an option was set (contract P2).
        Map<String, Object> params = draft.snapshot();
        if (!params.isEmpty()) {
            query.append("&params=").append(LabelZoomClient.encode(Json.write(params)));
        }

        String uri = client.apiUri(
                "/api/v3/printers/" + PrintWriters.segment(printerId) + "/print?" + query);
        HttpTransport.Request request = client.request(
                "POST", uri, PrintWriters.JSON_MEDIA_TYPE, draft.body, draft.contentType,
                PrintWriters.keyForCall(idempotencyKey));
        return client.send(request, response -> PrintJob.fromJson(PrintResponses.read(response)));
    }
}
