package com.labelzoom.sdk;

import java.util.Locale;
import java.util.Map;

/**
 * A format the LabelZoom API can convert <em>to</em>.
 *
 * <p>EPL, TSPL and DPL are intentionally absent — the server accepts them as sources only, so
 * asking for one is a compile error rather than a runtime 404.
 */
public enum TargetFormat {

    /** Zebra Programming Language. All labels are concatenated. */
    ZPL("zpl"),

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

    private static final Map<String, TargetFormat> BY_TOKEN = Map.of(
            "zpl", ZPL, "xml", XML, "json", JSON, "pdf", PDF,
            "png", PNG, "bmp", BMP, "gif", GIF, "jpeg", JPEG);

    /** Resolves a wire token to its enum constant, case-insensitively. */
    public static TargetFormat fromToken(String token) {
        TargetFormat format = BY_TOKEN.get(token.toLowerCase(Locale.ROOT));
        if (format == null) {
            throw new IllegalArgumentException("Unknown target format: " + token);
        }
        return format;
    }
}
