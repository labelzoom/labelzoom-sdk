package com.labelzoom.sdk;

import java.util.ArrayList;
import java.util.List;
import java.util.Map;

/**
 * The source stage: options that change how the source document is <b>read</b>, followed by the
 * choice of target format.
 *
 * <p>A conversion is built in two stages. {@code from*} picks the source and returns a source-stage
 * builder; {@code to*} picks the target and returns a target-stage builder that executes the
 * request. Each stage offers only the options that act on its side of the conversion, and a named
 * {@code from*} offers only the options for that format.
 *
 * @param <S> the concrete builder, so each method returns it unchanged
 */
public abstract class SourceStage<S extends SourceStage<S>> {

    final ConversionDraft draft;

    SourceStage(ConversionDraft draft) {
        this.draft = draft;
    }

    @SuppressWarnings("unchecked")
    private S self() {
        return (S) this;
    }

    /**
     * Label dimensions <b>in inches</b>. They replace whatever size the source declares; omit this to
     * use the declared size, or to detect one from the content.
     *
     * @param widthInches width in inches — not dots, not millimeters
     * @param heightInches height in inches
     */
    public S withLabelSize(float widthInches, float heightInches) {
        ParameterWriters.labelSize(draft, widthInches, heightInches);
        return self();
    }

    /**
     * Converts only one label of a multi-label source, on every plan, identified by a <b>0-based</b>
     * index. For an image or PDF it counts labels after any split, page by page.
     *
     * @param zeroBasedIndex 0 selects the first label
     */
    public S withLabelIndex(int zeroBasedIndex) {
        ParameterWriters.labelIndex(draft, zeroBasedIndex);
        return self();
    }

    /**
     * Supplies data to fill the label's variable fields. <b>Each record produces one label.</b>
     *
     * @param records maps whose keys are the label's variable field names
     */
    @SafeVarargs
    public final S withData(Map<String, ?>... records) {
        if (records == null || records.length == 0) {
            throw new LabelZoomValidationException(
                    "data", "Provide at least one data record, or omit this call entirely.");
        }
        // Copied element-by-element rather than via List.of(records): passing a generic array to
        // another varargs method re-raises the heap-pollution warning this method asserts against.
        List<Map<String, ?>> copy = new ArrayList<>(records.length);
        for (Map<String, ?> record : records) {
            copy.add(record);
        }
        return withData(copy);
    }

    /** Supplies data to fill the label's variable fields. Each element produces one label. */
    public S withData(List<? extends Map<String, ?>> records) {
        ParameterWriters.data(draft, records);
        return self();
    }

    /** Selects the target format. */
    public DynamicTargetBuilder to(TargetFormat target) {
        return new DynamicTargetBuilder(draft, target);
    }

    /** Converts to ZPL. All labels are concatenated into one document. */
    public ZplTargetBuilder toZpl() {
        return new ZplTargetBuilder(draft);
    }

    /**
     * Converts to EPL. All labels are concatenated into one document. Read
     * {@link ConversionResult#bytes()} rather than {@link ConversionResult#text()}: EPL's
     * {@code GW} command inlines raw binary.
     */
    public TargetBuilder toEpl() {
        return new TargetBuilder(draft, TargetFormat.EPL);
    }

    /**
     * Converts to Intermec IPL. All labels are concatenated into one document. As with
     * {@link #toEpl()}, prefer {@link ConversionResult#bytes()}: commands are framed in
     * {@code STX}/{@code ETX}.
     */
    public TargetBuilder toIpl() {
        return new TargetBuilder(draft, TargetFormat.IPL);
    }

    /**
     * Converts to TSPL. All labels are concatenated into one document. As with {@link #toEpl()},
     * prefer {@link ConversionResult#bytes()}.
     */
    public TargetBuilder toTspl() {
        return new TargetBuilder(draft, TargetFormat.TSPL);
    }

    /** Converts to Datamax DPL. All labels are concatenated into one document. */
    public TargetBuilder toDpl() {
        return new TargetBuilder(draft, TargetFormat.DPL);
    }

    /**
     * Converts to SATO SBPL. All labels are concatenated into one document. As with
     * {@link #toEpl()}, prefer {@link ConversionResult#bytes()}: every command is framed with
     * {@code ESC}.
     */
    public TargetBuilder toSbpl() {
        return new TargetBuilder(draft, TargetFormat.SBPL);
    }

    /**
     * Converts to Zebra CPCL, for Zebra's mobile printers. All labels are concatenated into one
     * document. Graphics are written as hex, so the output is plain text.
     */
    public TargetBuilder toCpcl() {
        return new TargetBuilder(draft, TargetFormat.CPCL);
    }

    /** Converts to LabelZoom XML. Returns the first label only. */
    public TargetBuilder toXml() {
        return new TargetBuilder(draft, TargetFormat.XML);
    }

    /** Converts to LabelZoom JSON. First label only; requires a paid license. */
    public TargetBuilder toJson() {
        return new TargetBuilder(draft, TargetFormat.JSON);
    }

    /** Converts to PDF, one page per label. */
    public PdfTargetBuilder toPdf() {
        return new PdfTargetBuilder(draft);
    }

    /** Converts to a PNG image. Returns the first label only. */
    public ImageTargetBuilder toPng() {
        return new ImageTargetBuilder(draft, TargetFormat.PNG);
    }

    /** Converts to a BMP image. Returns the first label only. */
    public ImageTargetBuilder toBmp() {
        return new ImageTargetBuilder(draft, TargetFormat.BMP);
    }

    /** Converts to a GIF image. Returns the first label only. */
    public ImageTargetBuilder toGif() {
        return new ImageTargetBuilder(draft, TargetFormat.GIF);
    }

    /** Converts to a JPEG image. Returns the first label only. */
    public ImageTargetBuilder toJpeg() {
        return new ImageTargetBuilder(draft, TargetFormat.JPEG);
    }
}
