package com.labelzoom.sdk;

/**
 * Chooses the target format. One class covers all thirteen.
 *
 * <p>There is no {@code toUrl}: {@code URL} is a source-only fetch instruction, and
 * {@link TargetFormat} has no constant for it. Attempting one is a compile error rather than a
 * runtime 404.
 */
public final class ConversionSourceBuilder {

    private final LabelZoomClient client;
    private final SourceFormat source;
    private final byte[] body;
    private final String contentType;

    ConversionSourceBuilder(
            LabelZoomClient client, SourceFormat source, byte[] body, String contentType) {
        this.client = client;
        this.source = source;
        this.body = body;
        this.contentType = contentType;
    }

    /** Selects the target format. */
    public ConversionTargetBuilder to(TargetFormat target) {
        return new ConversionTargetBuilder(client, source, target, body, contentType);
    }

    /** Converts to ZPL. All labels are concatenated into one document. */
    public ConversionTargetBuilder toZpl() {
        return to(TargetFormat.ZPL);
    }

    /**
     * Converts to EPL. All labels are concatenated into one document. Read
     * {@link ConversionResult#bytes()} rather than {@link ConversionResult#text()}: EPL's
     * {@code GW} command inlines raw binary.
     */
    public ConversionTargetBuilder toEpl() {
        return to(TargetFormat.EPL);
    }

    /**
     * Converts to Intermec IPL. All labels are concatenated into one document. As with
     * {@link #toEpl()}, prefer {@link ConversionResult#bytes()}: commands are framed in
     * {@code STX}/{@code ETX}.
     */
    public ConversionTargetBuilder toIpl() {
        return to(TargetFormat.IPL);
    }

    /**
     * Converts to TSPL. All labels are concatenated into one document. As with {@link #toEpl()},
     * prefer {@link ConversionResult#bytes()}.
     */
    public ConversionTargetBuilder toTspl() {
        return to(TargetFormat.TSPL);
    }

    /** Converts to Datamax DPL. All labels are concatenated into one document. */
    public ConversionTargetBuilder toDpl() {
        return to(TargetFormat.DPL);
    }

    /**
     * Converts to SATO SBPL. All labels are concatenated into one document. As with
     * {@link #toEpl()}, prefer {@link ConversionResult#bytes()}: every command is framed with
     * {@code ESC}.
     */
    public ConversionTargetBuilder toSbpl() {
        return to(TargetFormat.SBPL);
    }

    /** Converts to LabelZoom XML. Returns the first label only. */
    public ConversionTargetBuilder toXml() {
        return to(TargetFormat.XML);
    }

    /** Converts to LabelZoom JSON. First label only; requires a paid license. */
    public ConversionTargetBuilder toJson() {
        return to(TargetFormat.JSON);
    }

    /** Converts to PDF, one page per label. */
    public ConversionTargetBuilder toPdf() {
        return to(TargetFormat.PDF);
    }

    /** Converts to a PNG image. Returns the first label only. */
    public ConversionTargetBuilder toPng() {
        return to(TargetFormat.PNG);
    }

    /** Converts to a BMP image. Returns the first label only. */
    public ConversionTargetBuilder toBmp() {
        return to(TargetFormat.BMP);
    }

    /** Converts to a GIF image. Returns the first label only. */
    public ConversionTargetBuilder toGif() {
        return to(TargetFormat.GIF);
    }

    /** Converts to a JPEG image. Returns the first label only. */
    public ConversionTargetBuilder toJpeg() {
        return to(TargetFormat.JPEG);
    }
}
