package com.labelzoom.sdk;

import java.util.Map;

/**
 * A format the LabelZoom API can convert <em>from</em>.
 *
 * <p>Deliberately a different type from {@link TargetFormat}. That is what makes {@link #EPL},
 * {@link #TSPL} and {@link #DPL} un-selectable as conversion targets: they are source-only on the
 * server, and there is simply no {@code TargetFormat.EPL} to name.
 */
public enum SourceFormat {

    /** Zebra Programming Language. */
    ZPL("zpl", "text/plain"),

    /** Eltron Programming Language. Source-only. */
    EPL("epl", "text/plain"),

    /** TSC Printer Language. Source-only. */
    TSPL("tspl", "text/plain"),

    /** Datamax Printer Language. Source-only. */
    DPL("dpl", "text/plain"),

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

    private static final Map<String, SourceFormat> BY_TOKEN = Map.ofEntries(
            Map.entry("zpl", ZPL), Map.entry("epl", EPL), Map.entry("tspl", TSPL),
            Map.entry("dpl", DPL), Map.entry("xml", XML), Map.entry("json", JSON),
            Map.entry("pdf", PDF), Map.entry("png", PNG), Map.entry("bmp", BMP),
            Map.entry("gif", GIF), Map.entry("jpeg", JPEG), Map.entry("jpg", JPG),
            Map.entry("url", URL));

    /** Resolves a wire token such as {@code "jpg"} to its enum constant, case-insensitively. */
    public static SourceFormat fromToken(String token) {
        SourceFormat format = BY_TOKEN.get(token.toLowerCase(java.util.Locale.ROOT));
        if (format == null) {
            throw new IllegalArgumentException("Unknown source format: " + token);
        }
        return format;
    }
}
