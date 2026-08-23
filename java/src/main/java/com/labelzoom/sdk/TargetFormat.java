package com.labelzoom.sdk;

import java.util.Locale;
import java.util.Map;

/**
 * A format the LabelZoom API can convert <em>to</em>.
 *
 * <p>{@code JPG} and {@code URL} are intentionally absent: {@code JPG} is an input spelling that
 * normalizes to {@link #JPEG}, and {@code URL} is a fetch instruction rather than a format, so
 * asking for one is a compile error rather than a runtime 404.
 */
public enum TargetFormat {

    /** Zebra Programming Language. All labels are concatenated. */
    ZPL("zpl"),

    /**
     * Eltron Programming Language. All labels are concatenated.
     *
     * <p>Read {@link ConversionResult#bytes()} rather than {@link ConversionResult#text()}: EPL's
     * {@code GW} graphics command inlines raw binary that a charset decode can corrupt.
     */
    EPL("epl"),

    /**
     * TSC printer language. All labels are concatenated. As with {@link #EPL}, the {@code BITMAP}
     * command inlines raw binary, so prefer {@link ConversionResult#bytes()}.
     */
    TSPL("tspl"),

    /** Datamax Printer Language. All labels are concatenated. */
    DPL("dpl"),

    /** LabelZoom XML. First label only. */
    XML("xml"),

    /** LabelZoom JSON. First label only. Requires a paid license. */
    JSON("json"),

    /** PDF document, one page per label. */
    PDF("pdf"),

    /** PNG image. First label only. */
    PNG("png"),

    /** BMP image. First label only. */
    BMP("bmp"),

    /** GIF image. First label only. */
    GIF("gif"),

    /** JPEG image. First label only. */
    JPEG("jpeg");

    private final String wireToken;

    TargetFormat(String wireToken) {
        this.wireToken = wireToken;
    }

    /** The token used in the request path. */
    public String wireToken() {
        return wireToken;
    }

    /**
     * Derived from {@link #values()} rather than hand-listed. Every target's wire token is unique,
     * so the derivation is total and cannot drift when a constant is added — which the hand-written
     * {@code Map.of} it replaced did, silently, the moment EPL/TSPL/DPL became targets.
     */
    private static final Map<String, TargetFormat> BY_TOKEN;

    static {
        Map<String, TargetFormat> byToken = new java.util.HashMap<>();
        for (TargetFormat format : values()) {
            byToken.put(format.wireToken, format);
        }
        BY_TOKEN = java.util.Collections.unmodifiableMap(byToken);
    }

    /** Resolves a wire token to its enum constant, case-insensitively. */
    public static TargetFormat fromToken(String token) {
        TargetFormat format = BY_TOKEN.get(token.toLowerCase(Locale.ROOT));
        if (format == null) {
            throw new IllegalArgumentException("Unknown target format: " + token);
        }
        return format;
    }
}
