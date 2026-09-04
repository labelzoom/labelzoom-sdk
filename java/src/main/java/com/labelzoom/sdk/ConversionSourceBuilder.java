package com.labelzoom.sdk;

import java.util.LinkedHashMap;
import java.util.Map;

/**
 * Chooses the target format. One class covers all eleven.
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
    private final Map<String, Object> params;

    ConversionSourceBuilder(
            LabelZoomClient client, SourceFormat source, byte[] body, String contentType) {
        this(client, source, body, contentType, new LinkedHashMap<>());
    }

    private ConversionSourceBuilder(
            LabelZoomClient client,
            SourceFormat source,
            byte[] body,
            String contentType,
            Map<String, Object> params) {
        this.client = client;
        this.source = source;
        this.body = body;
        this.contentType = contentType;
        this.params = params;
    }

    /**
     * How the <b>source's</b> absolute positions are interpreted, in dots per inch: dots for
     * printer languages, pixels for bitmap images, an override of the document's dpi for LabelZoom
     * XML/JSON. Not applicable to PDF sources (vector). This is the source-side dpi; use
     * {@link ConversionTargetBuilder#withDpi(int)} to author the output at a resolution, and both
     * may be set when the chosen format pair supports a dpi on each side.
     */
    public ConversionSourceBuilder withDpi(int dpi) {
        if (dpi <= 0) {
            throw new LabelZoomValidationException("dpi", "DPI must be greater than zero.");
        }
        params.put("sourceDpi", dpi);
        return this;
    }

    /** Selects the target format. */
    public ConversionTargetBuilder to(TargetFormat target) {
        return new ConversionTargetBuilder(client, source, target, body, contentType, params);
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
