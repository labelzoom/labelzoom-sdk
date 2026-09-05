package com.labelzoom.sdk;

import java.util.Map;

/**
 * A format the LabelZoom API can convert <em>from</em>.
 *
 * <p>Deliberately a different type from {@link TargetFormat}. That is what makes {@link #JPG} and
 * {@link #URL} un-selectable as conversion targets: {@code JPG} is an input spelling of
 * {@code jpeg}, {@code URL} is a fetch instruction rather than a format, and there is simply no
 * {@code TargetFormat.URL} to name.
 */
public enum SourceFormat {

    /** Zebra Programming Language. */
    ZPL("zpl", "text/plain"),

    /** Eltron Programming Language. */
    EPL("epl", "text/plain"),

    /** Intermec Printer Language. */
    IPL("ipl", "text/plain"),

    /** TSC Printer Language. */
    TSPL("tspl", "text/plain"),

    /** Datamax Printer Language. */
    DPL("dpl", "text/plain"),

    /** SATO Barcode Printer Language. */
    SBPL("sbpl", "text/plain"),

    /** LabelZoom XML. */
    XML("xml", "application/xml"),

    /** LabelZoom JSON. */
    JSON("json", "application/json"),

    /** PDF document. */
    PDF("pdf", "application/pdf"),

    /** PNG image. */
    PNG("png", "image/png"),

    /** BMP image. */
    BMP("bmp", "image/bmp"),

    /** GIF image. */
    GIF("gif", "image/gif"),

    /** JPEG image. */
    JPEG("jpeg", "image/jpeg"),

    /** Alias for {@link #JPEG}; normalized to {@code jpeg} on the wire. */
    JPG("jpeg", "image/jpeg"),

    /**
     * A URL, sent as the request body. The <em>server</em> then fetches it and converts whatever it
     * finds.
     *
     * <p>This performs a server-side fetch of a URL you supply. Validate it first if it came from
     * untrusted input.
     */
    URL("url", "text/plain");

    private final String wireToken;
    private final String mediaType;

    SourceFormat(String wireToken, String mediaType) {
        this.wireToken = wireToken;
        this.mediaType = mediaType;
    }

    /** The token used in the request path. {@code JPG} and {@code JPEG} both yield {@code jpeg}. */
    public String wireToken() {
        return wireToken;
    }

    /** The {@code Content-Type} a request carrying this format must send. */
    public String mediaType() {
        return mediaType;
    }

    /**
     * Derived from {@link #values()} rather than hand-listed.
     *
     * <p>Keyed on the constant's own name, not {@link #wireToken()}: {@code JPG} and {@code JPEG}
     * share the wire token {@code jpeg}, but a caller who wrote {@code "jpg"} must still resolve.
     * Every constant's name is its caller-facing token, so the derivation is total.
     *
     * <p>This was a hand-written {@code Map.ofEntries} that had to be edited in lockstep with the
     * constants above — the same shape that silently broke {@link TargetFormat} when EPL, TSPL and
     * DPL became targets. Deriving it means adding a format cannot forget this map.
     */
    private static final Map<String, SourceFormat> BY_TOKEN;

    static {
        Map<String, SourceFormat> byToken = new java.util.HashMap<>();
        for (SourceFormat format : values()) {
            byToken.put(format.name().toLowerCase(java.util.Locale.ROOT), format);
        }
        BY_TOKEN = java.util.Collections.unmodifiableMap(byToken);
    }

    /** Resolves a wire token such as {@code "jpg"} to its enum constant, case-insensitively. */
    public static SourceFormat fromToken(String token) {
        SourceFormat format = BY_TOKEN.get(token.toLowerCase(java.util.Locale.ROOT));
        if (format == null) {
            throw new IllegalArgumentException("Unknown source format: " + token);
        }
        return format;
    }
}
