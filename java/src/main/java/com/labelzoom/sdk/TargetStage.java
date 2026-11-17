package com.labelzoom.sdk;

/**
 * The target stage: options that change the <b>output</b>, followed by {@link #execute()}.
 *
 * <p>The options here apply to every target. Format-specific ones live on the builder that
 * {@code to*} returns for that format, such as {@link ZplTargetBuilder}. How an image or PDF source
 * is rasterized is a source option: see {@link RasterOptions}. An option the server doesn't apply to
 * the chosen format pair is ignored rather than rejected.
 *
 * @param <T> the concrete builder, so each method returns it unchanged
 */
public abstract class TargetStage<T extends TargetStage<T>> {

    private final ConversionDraft draft;
    private final TargetFormat target;

    TargetStage(ConversionDraft draft, TargetFormat target) {
        this.draft = draft;
        this.target = target;
    }

    final ConversionDraft draft() {
        return draft;
    }

    @SuppressWarnings("unchecked")
    private T self() {
        return (T) this;
    }

    /**
     * Rotation in degrees clockwise. Must be a multiple of 90. The server default is 0. It turns the
     * finished label, for every format pair.
     *
     * @param rotation 0, 90, 180 or 270
     */
    public T withRotation(int rotation) {
        ParameterWriters.rotation(draft, rotation);
        return self();
    }

    /** Requests a watermark. Output is watermarked regardless on the anonymous free tier. */
    public T withWatermark(boolean watermark) {
        draft.params.put("watermark", watermark);
        return self();
    }

    /**
     * Selects a printer dialect, for example {@code moca} for Blue Yonder WMS. Requires a paid
     * license; without one the request fails with a 403 whose
     * {@link LabelZoomForbiddenException#isPaidFeature()} is set.
     */
    public T withDialect(String dialect) {
        ParameterWriters.dialect(draft, dialect);
        return self();
    }

    /**
     * Sets a parameter the SDK does not model yet. Unknown keys are ignored by the server, so this is
     * a safe forward-compatibility escape hatch.
     */
    public T withParameter(String key, Object value) {
        ParameterWriters.parameter(draft, key, value);
        return self();
    }

    /** Adds a raw query-string parameter alongside {@code params}. */
    public T withRawQueryParameter(String key, String value) {
        ParameterWriters.rawQueryParameter(draft, key, value);
        return self();
    }

    /**
     * Executes the conversion. The options are copied when this is called, so a builder can be
     * executed again, or changed and executed again, without affecting a request in flight.
     *
     * @throws LabelZoomException if the API returns a non-2xx response
     */
    public ConversionResult execute() {
        return draft.client.execute(draft.source, target, draft.body, draft.contentType,
                draft.snapshot(), new java.util.LinkedHashMap<>(draft.rawQuery));
    }
}
