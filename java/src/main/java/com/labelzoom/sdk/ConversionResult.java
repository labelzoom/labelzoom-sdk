package com.labelzoom.sdk;

import java.io.IOException;
import java.nio.charset.Charset;
import java.nio.charset.IllegalCharsetNameException;
import java.nio.charset.StandardCharsets;
import java.nio.charset.UnsupportedCharsetException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Optional;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

/**
 * The outcome of a successful conversion.
 *
 * <p>{@link #bytes()} is authoritative. PDF, PNG, BMP, GIF and JPEG targets are binary, so an API
 * that returned only a string would silently corrupt five of the eight targets.
 */
public final class ConversionResult {

    private static final Pattern CHARSET = Pattern.compile("charset=([^;\\s]+)", Pattern.CASE_INSENSITIVE);

    private final byte[] bytes;
    private final String contentType;
    private final int statusCode;
    private final String requestId;

    ConversionResult(byte[] bytes, String contentType, int statusCode, String requestId) {
        this.bytes = bytes;
        this.contentType = contentType;
        this.statusCode = statusCode;
        this.requestId = requestId;
    }

    /** The converted document, exactly as the server returned it. */
    public byte[] bytes() {
        return bytes.clone();
    }

    /** The response {@code Content-Type}, including any charset parameter. */
    public Optional<String> contentType() {
        return Optional.ofNullable(contentType);
    }

    /** The HTTP status code, always 2xx here. */
    public int statusCode() {
        return statusCode;
    }

    /**
     * The {@code X-LZ-Request-Id} response header, or empty if the server did not send one. Quote
     * it when contacting LabelZoom support.
     */
    public Optional<String> requestId() {
        return Optional.ofNullable(requestId);
    }

    /**
     * {@link #bytes()} decoded using the response charset, defaulting to UTF-8.
     *
     * <p>Meaningful for the ZPL, XML and JSON targets. Decoding a PNG will succeed and produce
     * nonsense — use {@link #bytes()} for binary targets.
     */
    public String text() {
        return new String(bytes, charset());
    }

    private Charset charset() {
        if (contentType != null) {
            Matcher matcher = CHARSET.matcher(contentType);
            if (matcher.find()) {
                try {
                    return Charset.forName(matcher.group(1).replace("\"", ""));
                } catch (IllegalCharsetNameException | UnsupportedCharsetException e) {
                    // An unrecognized charset is not worth failing an otherwise good conversion.
                }
            }
        }
        return StandardCharsets.UTF_8;
    }

    /** Writes {@link #bytes()} to a file, overwriting any existing one. */
    public void save(Path path) throws IOException {
        Files.write(path, bytes);
    }
}
