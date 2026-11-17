package com.labelzoom.sdk;

import java.io.IOException;
import java.io.InputStream;
import java.io.UncheckedIOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;

/**
 * Chooses the source document. Returned by {@link LabelZoomClient#convert()}.
 *
 * <p>A named {@code from*} method returns a source stage typed for that format, so only the options
 * that apply to it are offered: {@link #fromPdf(byte[])} returns a {@link PdfSourceBuilder} with
 * {@link PdfSourceBuilder#withPage(int)}, which {@link #fromZpl(String)} doesn't have. The methods
 * that take a {@link SourceFormat} pick the format at run time and return a
 * {@link DynamicSourceBuilder}, which offers every source option. No subclass knows a format: the
 * format is fixed here, once, and every option is written by one shared function (contract 1.2).
 */
public final class ConversionRequestBuilder {

    private final LabelZoomClient client;

    ConversionRequestBuilder(LabelZoomClient client) {
        this.client = client;
    }

    /** Uses raw bytes as the source document. */
    public DynamicSourceBuilder from(SourceFormat format, byte[] body) {
        return new DynamicSourceBuilder(validated(format, body));
    }

    private ConversionDraft validated(SourceFormat format, byte[] body) {
        if (body == null) {
            throw new LabelZoomValidationException("body", "Source body cannot be null.");
        }
        if (body.length == 0) {
            // The gateway rejects a zero-length body with 400. Catching it here saves a round trip
            // and says something more useful than "Request body is required".
            throw new LabelZoomValidationException(
                    "body", "Source body cannot be empty; the API rejects zero-length requests.");
        }
        return new ConversionDraft(client, format, body, format.mediaType());
    }

    /** Uses text as the source document, encoded as UTF-8. */
    public DynamicSourceBuilder from(SourceFormat format, String body) {
        return from(format, utf8(body));
    }

    private static byte[] utf8(String body) {
        if (body == null) {
            throw new LabelZoomValidationException("body", "Source body cannot be null.");
        }
        return body.getBytes(StandardCharsets.UTF_8);
    }

    /**
     * Reads a stream to completion and uses it as the source document.
     *
     * <p>Buffered rather than streamed, because a retried request needs to send the same body again
     * and a consumed stream cannot be replayed.
     */
    public DynamicSourceBuilder from(SourceFormat format, InputStream body) {
        return from(format, readAll(body));
    }

    private static byte[] readAll(InputStream body) {
        if (body == null) {
            throw new LabelZoomValidationException("body", "Source stream cannot be null.");
        }
        try {
            return Compat.readAllBytes(body);
        } catch (IOException e) {
            throw new UncheckedIOException("Could not read the source stream", e);
        }
    }

    /** Reads a file from disk and uses it as the source document. */
    public DynamicSourceBuilder fromFile(SourceFormat format, Path path) {
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
    public DynamicSourceBuilder fromBase64Text(SourceFormat format, String base64) {
        if (base64 == null || base64.isEmpty()) {
            throw new LabelZoomValidationException("body", "Base64 body cannot be null or empty.");
        }
        return new DynamicSourceBuilder(new ConversionDraft(
                client, format, base64.getBytes(StandardCharsets.UTF_8), "text/plain"));
    }

    /** Converts from ZPL. */
    public ZplSourceBuilder fromZpl(String zpl) {
        return new ZplSourceBuilder(validated(SourceFormat.ZPL, utf8(zpl)));
    }

    /** Converts from EPL/EPL2. */
    public PrinterLanguageSourceBuilder fromEpl(String epl) {
        return new PrinterLanguageSourceBuilder(validated(SourceFormat.EPL, utf8(epl)));
    }

    /** Converts from Intermec IPL. */
    public PrinterLanguageSourceBuilder fromIpl(String ipl) {
        return new PrinterLanguageSourceBuilder(validated(SourceFormat.IPL, utf8(ipl)));
    }

    /** Converts from TSPL/TSPL2. */
    public PrinterLanguageSourceBuilder fromTspl(String tspl) {
        return new PrinterLanguageSourceBuilder(validated(SourceFormat.TSPL, utf8(tspl)));
    }

    /** Converts from DPL. */
    public PrinterLanguageSourceBuilder fromDpl(String dpl) {
        return new PrinterLanguageSourceBuilder(validated(SourceFormat.DPL, utf8(dpl)));
    }

    /** Converts from SATO SBPL. */
    public PrinterLanguageSourceBuilder fromSbpl(String sbpl) {
        return new PrinterLanguageSourceBuilder(validated(SourceFormat.SBPL, utf8(sbpl)));
    }

    /** Converts from LabelZoom XML. */
    public SourceBuilder fromXml(String xml) {
        return new SourceBuilder(validated(SourceFormat.XML, utf8(xml)));
    }

    /** Converts from LabelZoom JSON. */
    public SourceBuilder fromJson(String json) {
        return new SourceBuilder(validated(SourceFormat.JSON, utf8(json)));
    }

    /** Converts from a PDF document. */
    public PdfSourceBuilder fromPdf(byte[] pdf) {
        return new PdfSourceBuilder(validated(SourceFormat.PDF, pdf));
    }

    /** Converts from a PDF document. */
    public PdfSourceBuilder fromPdf(InputStream pdf) {
        return fromPdf(readAll(pdf));
    }

    /** Converts from a PNG image. */
    public ImageSourceBuilder fromPng(byte[] png) {
        return new ImageSourceBuilder(validated(SourceFormat.PNG, png));
    }

    /** Converts from a BMP image. */
    public ImageSourceBuilder fromBmp(byte[] bmp) {
        return new ImageSourceBuilder(validated(SourceFormat.BMP, bmp));
    }

    /** Converts from a GIF image. */
    public ImageSourceBuilder fromGif(byte[] gif) {
        return new ImageSourceBuilder(validated(SourceFormat.GIF, gif));
    }

    /** Converts from a JPEG image. */
    public ImageSourceBuilder fromJpeg(byte[] jpeg) {
        return new ImageSourceBuilder(validated(SourceFormat.JPEG, jpeg));
    }

    /**
     * Has the <em>server</em> fetch a URL and convert whatever it finds there.
     *
     * <p>Validate the URL before passing it if it came from untrusted input.
     */
    public DynamicSourceBuilder fromUrl(String url) {
        if (url == null || Compat.isBlank(url)) {
            throw new LabelZoomValidationException("body", "URL cannot be null or blank.");
        }
        return from(SourceFormat.URL, url);
    }
}
