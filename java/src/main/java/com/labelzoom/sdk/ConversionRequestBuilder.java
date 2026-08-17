package com.labelzoom.sdk;

import java.io.IOException;
import java.io.InputStream;
import java.io.UncheckedIOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;

/**
 * Chooses the source document.
 *
 * <p>There is one source builder and one target builder for all 12 x 8 format combinations, not a
 * class per format. The named {@code from*} methods are one-line delegations to
 * {@link #from(SourceFormat, byte[])}: they exist for discoverability and, holding no logic of
 * their own, cannot drift away from the format table.
 */
public final class ConversionRequestBuilder {

    private final LabelZoomClient client;

    ConversionRequestBuilder(LabelZoomClient client) {
        this.client = client;
    }

    /** Uses raw bytes as the source document. */
    public ConversionSourceBuilder from(SourceFormat format, byte[] body) {
        if (body == null) {
            throw new LabelZoomValidationException("body", "Source body cannot be null.");
        }
        if (body.length == 0) {
            // The gateway rejects a zero-length body with 400. Catching it here saves a round trip
            // and says something more useful than "Request body is required".
            throw new LabelZoomValidationException(
                    "body", "Source body cannot be empty; the API rejects zero-length requests.");
        }
        return new ConversionSourceBuilder(client, format, body, format.mediaType());
    }

    /** Uses text as the source document, encoded as UTF-8. */
    public ConversionSourceBuilder from(SourceFormat format, String body) {
        if (body == null) {
            throw new LabelZoomValidationException("body", "Source body cannot be null.");
        }
        return from(format, body.getBytes(StandardCharsets.UTF_8));
    }

    /**
     * Reads a stream to completion and uses it as the source document.
     *
     * <p>Buffered rather than streamed, because a retried request needs to send the same body again
     * and a consumed stream cannot be replayed.
     */
    public ConversionSourceBuilder from(SourceFormat format, InputStream body) {
        if (body == null) {
            throw new LabelZoomValidationException("body", "Source stream cannot be null.");
        }
        try {
            return from(format, body.readAllBytes());
        } catch (IOException e) {
            throw new UncheckedIOException("Could not read the source stream", e);
        }
    }

    /** Reads a file from disk and uses it as the source document. */
    public ConversionSourceBuilder fromFile(SourceFormat format, Path path) {
        if (path == null) {
            throw new LabelZoomValidationException("path", "Path cannot be null.");
        }
        try {
            return from(format, Files.readAllBytes(path));
        } catch (IOException e) {
            throw new UncheckedIOException("Could not read " + path, e);
        }
    }

    /**
     * Uses a base64-encoded document as the source, sent as {@code text/plain}.
     *
     * <p>The API accepts PDF and image sources either as raw bytes with their own media type or as
     * base64 text. Prefer {@link #from(SourceFormat, byte[])}; this exists for callers whose
     * transport has already base64-encoded the payload.
     */
    public ConversionSourceBuilder fromBase64Text(SourceFormat format, String base64) {
        if (base64 == null || base64.isEmpty()) {
            throw new LabelZoomValidationException("body", "Base64 body cannot be null or empty.");
        }
        return new ConversionSourceBuilder(
                client, format, base64.getBytes(StandardCharsets.UTF_8), "text/plain");
    }

    /** Converts from ZPL. */
    public ConversionSourceBuilder fromZpl(String zpl) {
        return from(SourceFormat.ZPL, zpl);
    }

    /** Converts from EPL/EPL2. Source-only on the server. */
    public ConversionSourceBuilder fromEpl(String epl) {
        return from(SourceFormat.EPL, epl);
    }

    /** Converts from TSPL/TSPL2. Source-only on the server. */
    public ConversionSourceBuilder fromTspl(String tspl) {
        return from(SourceFormat.TSPL, tspl);
    }

    /** Converts from DPL. Source-only on the server. */
    public ConversionSourceBuilder fromDpl(String dpl) {
        return from(SourceFormat.DPL, dpl);
    }

    /** Converts from LabelZoom XML. */
    public ConversionSourceBuilder fromXml(String xml) {
        return from(SourceFormat.XML, xml);
    }

    /** Converts from LabelZoom JSON. */
    public ConversionSourceBuilder fromJson(String json) {
        return from(SourceFormat.JSON, json);
    }

    /** Converts from a PDF document. */
    public ConversionSourceBuilder fromPdf(byte[] pdf) {
        return from(SourceFormat.PDF, pdf);
    }

    /** Converts from a PDF document. */
    public ConversionSourceBuilder fromPdf(InputStream pdf) {
        return from(SourceFormat.PDF, pdf);
    }

    /** Converts from a PNG image. */
    public ConversionSourceBuilder fromPng(byte[] png) {
        return from(SourceFormat.PNG, png);
    }

    /** Converts from a BMP image. */
    public ConversionSourceBuilder fromBmp(byte[] bmp) {
        return from(SourceFormat.BMP, bmp);
    }

    /** Converts from a GIF image. */
    public ConversionSourceBuilder fromGif(byte[] gif) {
        return from(SourceFormat.GIF, gif);
    }

    /** Converts from a JPEG image. */
    public ConversionSourceBuilder fromJpeg(byte[] jpeg) {
        return from(SourceFormat.JPEG, jpeg);
    }

    /**
     * Has the <em>server</em> fetch a URL and convert whatever it finds there.
     *
     * <p>Validate the URL before passing it if it came from untrusted input.
     */
    public ConversionSourceBuilder fromUrl(String url) {
        if (url == null || url.isBlank()) {
            throw new LabelZoomValidationException("body", "URL cannot be null or blank.");
        }
        return from(SourceFormat.URL, url);
    }
}
