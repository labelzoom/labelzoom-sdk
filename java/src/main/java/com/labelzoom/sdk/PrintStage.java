package com.labelzoom.sdk;

import java.util.ArrayList;
import java.util.List;
import java.util.Map;

/**
 * The print source stage: options that change how the document is <b>read</b>, then the print
 * options, then {@link #send()}.
 *
 * <p>It mirrors {@link SourceStage} without the {@code to*} transitions, because the printer picks
 * the output language. Each format's print builder offers exactly the source options its convert
 * builder does, through the same shared writers, so {@link PdfPrintBuilder} has
 * {@link PdfPrintBuilder#withPage(int)} as {@link PdfSourceBuilder} does. An option the server
 * doesn't apply to the format is ignored rather than rejected.
 *
 * @param <S> the concrete builder, so each method returns it unchanged
 */
public abstract class PrintStage<S extends PrintStage<S>> {

    final PrintDraft draft;

    PrintStage(PrintDraft draft) {
        this.draft = draft;
    }

    /** The document and its source options, which {@link ParameterWriters} writes into. */
    final ConversionDraft document() {
        return draft.document;
    }

    @SuppressWarnings("unchecked")
    private S self() {
        return (S) this;
    }

    /**
     * Label dimensions <b>in inches</b>. They replace whatever size the document declares; content
     * past them is clipped, as it is on the printer. Omit this to use the declared size, or to detect
     * one from the content.
     *
     * @param widthInches width in inches — not dots, not millimeters
     * @param heightInches height in inches
     */
    public S withLabelSize(float widthInches, float heightInches) {
        ParameterWriters.labelSize(document(), widthInches, heightInches);
        return self();
    }

    /**
     * Prints only one label of a multi-label document, identified by a <b>0-based</b> index.
     *
     * @param zeroBasedIndex 0 selects the first label
     */
    public S withLabelIndex(int zeroBasedIndex) {
        ParameterWriters.labelIndex(document(), zeroBasedIndex);
        return self();
    }

    /**
     * Supplies data to fill the document's variable fields. <b>Each record prints one label.</b>
     *
     * @param records maps whose keys are the label's variable field names
     */
    @SafeVarargs
    public final S withData(Map<String, ?>... records) {
        if (records == null || records.length == 0) {
            throw new LabelZoomValidationException(
                    "data", "Provide at least one data record, or omit this call entirely.");
        }
        List<Map<String, ?>> copy = new ArrayList<>(records.length);
        for (Map<String, ?> record : records) {
            copy.add(record);
        }
        return withData(copy);
    }

    /** Supplies data to fill the document's variable fields. Each element prints one label. */
    public S withData(List<? extends Map<String, ?>> records) {
        ParameterWriters.data(document(), records);
        return self();
    }

    /**
     * Sends the whole job to the printer this many times, from 1 to 9999. It multiplies a quantity
     * in the document: {@code ^PQ3} with 5 copies prints 15 labels.
     */
    public S withCopies(int copies) {
        draft.copies = PrintWriters.copies(copies);
        return self();
    }

    /**
     * Makes the print safe to repeat: a second request with the same key returns the original job
     * instead of printing again. Use something unique to the label, such as an order number.
     *
     * <p>Without it the SDK generates a key for each call to {@link #send()}, which makes the SDK's
     * own retries safe but not a repeat of the whole call. Keys are scoped to the account; a
     * deliberate reprint needs a new one.
     */
    public S withIdempotencyKey(String key) {
        draft.idempotencyKey = PrintWriters.idempotencyKey(key);
        return self();
    }

    /**
     * Sends the document to the printer. The options are copied when this is called, so a builder
     * can be sent again; each call is a separate print with its own generated key.
     *
     * @return the job the server created, or the original job when the key was used before
     * @throws LabelZoomException if the API returns a non-2xx response
     */
    public PrintJob send() {
        ConversionDraft document = draft.document;
        LabelZoomClient client = document.client;

        StringBuilder query = new StringBuilder("sourceFormat=")
                .append(LabelZoomClient.encode(document.source.wireToken()));
        if (draft.copies != null) {
            query.append("&copies=").append(draft.copies);
        }
        // An empty params would make the server convert a document that's already in the
        // printer's language, so it is sent only when a source option was set (contract P2).
        Map<String, Object> params = document.snapshot();
        if (!params.isEmpty()) {
            query.append("&params=").append(LabelZoomClient.encode(Json.write(params)));
        }

        String uri = client.apiUri(
                "/api/v3/printers/" + PrintWriters.segment(draft.printerId) + "/print?" + query);
        HttpTransport.Request request = client.request(
                "POST", uri, PrintWriters.JSON_MEDIA_TYPE, document.body, document.contentType,
                PrintWriters.keyForCall(draft.idempotencyKey));
        return client.send(request, response -> PrintJob.fromJson(PrintResponses.read(response)));
    }
}
