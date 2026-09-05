package com.labelzoom.sdk;

import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * Configures and executes a conversion.
 *
 * <p>Every {@code with*} method records its value and is actually sent. Only options you set are
 * serialized — the SDK never fills in a client-side default, so a change to a server default
 * reaches you without an SDK upgrade.
 */
public final class ConversionTargetBuilder {

    private final LabelZoomClient client;
    private final SourceFormat source;
    private final TargetFormat target;
    private final byte[] body;
    private final String contentType;
    private final Map<String, Object> params;
    private final Map<String, String> rawQuery = new LinkedHashMap<>();

    ConversionTargetBuilder(
            LabelZoomClient client,
            SourceFormat source,
            TargetFormat target,
            byte[] body,
            String contentType) {
        this(client, source, target, body, contentType, new LinkedHashMap<>());
    }

    ConversionTargetBuilder(
            LabelZoomClient client,
            SourceFormat source,
            TargetFormat target,
            byte[] body,
            String contentType,
            Map<String, Object> params) {
        this.client = client;
        this.source = source;
        this.target = target;
        this.body = body;
        this.contentType = contentType;
        this.params = params;
    }

    /**
     * The resolution the <b>output</b> is authored at, in dots per inch. The server default is 203.
     * This is the target-side dpi; use {@link ConversionSourceBuilder#withDpi(int)} to declare how
     * the source's positions are interpreted, and both may be set when the chosen format pair
     * supports a dpi on each side.
     */
    public ConversionTargetBuilder withDpi(int dpi) {
        if (dpi <= 0) {
            throw new LabelZoomValidationException("dpi", "DPI must be greater than zero.");
        }
        params.put("targetDpi", dpi);
        return this;
    }

    /**
     * Rotation in degrees clockwise. Must be a multiple of 90. The server default is 0.
     *
     * @param rotation 0, 90, 180 or 270
     */
    public ConversionTargetBuilder withRotation(int rotation) {
        // Rejected locally: the server would 400, and this is unambiguously a caller bug.
        if (rotation % 90 != 0) {
            throw new LabelZoomValidationException(
                    "rotation", "Rotation must be a multiple of 90 degrees, but was " + rotation + ".");
        }
        params.put("rotation", rotation);
        return this;
    }

    /** Scaling as a percentage. The server default is 100. */
    public ConversionTargetBuilder withScaling(float percent) {
        if (percent <= 0) {
            throw new LabelZoomValidationException("scaling", "Scaling must be greater than zero.");
        }
        params.put("scaling", percent);
        return this;
    }

    /** Colour handling. The server default is {@link ColorMode#GRAYSCALE}. */
    public ConversionTargetBuilder withColorMode(ColorMode mode) {
        params.put("colorMode", mode.wireToken());
        return this;
    }

    /** Luminance threshold from 0 to 100 used when reducing colour depth. Server default 70. */
    public ConversionTargetBuilder withDarkness(int darkness) {
        if (darkness < 0 || darkness > 100) {
            throw new LabelZoomValidationException(
                    "darkness", "Darkness must be between 0 and 100, but was " + darkness + ".");
        }
        params.put("darkness", darkness);
        return this;
    }

    /** Pixel offset of the top-left corner of the extracted region. */
    public ConversionTargetBuilder withPosition(int x, int y) {
        Map<String, Object> position = nested("position");
        position.put("x", x);
        position.put("y", y);
        return this;
    }

    /** Requests a watermark. Output is watermarked regardless on the anonymous free tier. */
    public ConversionTargetBuilder withWatermark(boolean watermark) {
        params.put("watermark", watermark);
        return this;
    }

    /**
     * Selects a printer dialect, for example {@code moca} for Blue Yonder WMS. Requires a paid
     * license; without one the request fails with a 403 whose
     * {@link LabelZoomForbiddenException#isPaidFeature()} is set.
     */
    public ConversionTargetBuilder withDialect(String dialect) {
        if (dialect == null || dialect.isBlank()) {
            throw new LabelZoomValidationException("dialect", "Dialect cannot be null or blank.");
        }
        params.put("dialect", dialect);
        return this;
    }

    /**
     * Label dimensions <b>in inches</b>, overriding whatever the source document implies.
     *
     * @param widthInches width in inches — not dots, not millimetres
     * @param heightInches height in inches
     */
    public ConversionTargetBuilder withLabelSize(float widthInches, float heightInches) {
        if (widthInches <= 0 || heightInches <= 0) {
            throw new LabelZoomValidationException(
                    "label", "Label width and height must be greater than zero.");
        }
        Map<String, Object> label = nested("label");
        label.put("width", widthInches);
        label.put("height", heightInches);
        return this;
    }

    /** How a source PDF is interpreted. The server default is {@link PdfConversionMode#IMAGE}. */
    public ConversionTargetBuilder withPdfConversionMode(PdfConversionMode mode) {
        nested("pdf").put("conversionMode", mode.wireToken());
        return this;
    }

    /**
     * Converts a single page of a source PDF, identified by a <b>0-based</b> index. Omit this to
     * convert every page.
     *
     * @param zeroBasedPageNumber 0 selects the first page
     */
    public ConversionTargetBuilder withPdfPage(int zeroBasedPageNumber) {
        if (zeroBasedPageNumber < 0) {
            throw new LabelZoomValidationException(
                    "pdf",
                    "Page number is 0-based and cannot be negative; 0 selects the first page.");
        }
        nested("pdf").put("pageNumber", zeroBasedPageNumber);
        return this;
    }

    /** ZPL commands the parser should skip, for example {@code ^PQ}. */
    public ConversionTargetBuilder withZplCommandsToIgnore(String... commands) {
        if (commands == null || commands.length == 0) {
            throw new LabelZoomValidationException(
                    "zpl", "Provide at least one command, or omit this call entirely.");
        }
        nested("zpl").put("commandsToIgnore", List.of(commands));
        return this;
    }

    /** Image compression used when writing ZPL. The server default is {@link ZplImageCompression#Z64}. */
    public ConversionTargetBuilder withZplImageCompression(ZplImageCompression compression) {
        nested("zpl").put("imageCompression", compression.wireToken());
        return this;
    }

    /**
     * Supplies data to fill the label's variable fields. <b>Each record produces one label.</b>
     *
     * @param records maps whose keys are the label's variable field names
     */
    @SafeVarargs
    public final ConversionTargetBuilder withData(Map<String, ?>... records) {
        if (records == null || records.length == 0) {
            throw new LabelZoomValidationException(
                    "data", "Provide at least one data record, or omit this call entirely.");
        }
        // Copied element-by-element rather than via List.of(records): passing a generic array
        // to another varargs method re-raises the heap-pollution warning this method already
        // asserts against.
        List<Map<String, ?>> copy = new ArrayList<>(records.length);
        for (Map<String, ?> record : records) {
            copy.add(record);
        }
        return withData(copy);
    }

    /** Supplies data to fill the label's variable fields. Each element produces one label. */
    public ConversionTargetBuilder withData(List<? extends Map<String, ?>> records) {
        if (records == null || records.isEmpty()) {
            throw new LabelZoomValidationException(
                    "data", "Provide at least one data record, or omit this call entirely.");
        }
        List<Object> normalized = new ArrayList<>(records.size());
        for (int i = 0; i < records.size(); i++) {
            Map<String, ?> record = records.get(i);
            if (record == null) {
                throw new LabelZoomValidationException(
                        "data", "data[" + i + "] is null; every entry must be an object.");
            }
            normalized.add(record);
        }
        params.put("data", normalized);
        return this;
    }

    /**
     * Sets a parameter the SDK does not model yet. Unknown keys are ignored by the server, so this
     * is a safe forward-compatibility escape hatch.
     */
    public ConversionTargetBuilder withParameter(String key, Object value) {
        if (key == null || key.isBlank()) {
            throw new LabelZoomValidationException("params", "Parameter key cannot be null or blank.");
        }
        params.put(key, value);
        return this;
    }

    /** Adds a raw query-string parameter alongside {@code params}. */
    public ConversionTargetBuilder withRawQueryParameter(String key, String value) {
        if (key == null || key.isBlank()) {
            throw new LabelZoomValidationException(
                    "params", "Query parameter key cannot be null or blank.");
        }
        rawQuery.put(key, value == null ? "" : value);
        return this;
    }

    /**
     * Executes the conversion.
     *
     * @throws LabelZoomException if the API returns a non-2xx response
     */
    public ConversionResult execute() {
        return client.execute(source, target, body, contentType,
                new LinkedHashMap<>(params), new LinkedHashMap<>(rawQuery));
    }

    @SuppressWarnings("unchecked")
    private Map<String, Object> nested(String group) {
        return (Map<String, Object>) params.computeIfAbsent(group, k -> new LinkedHashMap<String, Object>());
    }
}
