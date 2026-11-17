package com.labelzoom.sdk;

import java.io.IOException;
import java.io.InputStream;
import java.io.UncheckedIOException;
import java.nio.file.Files;
import java.nio.file.Path;

/**
 * Chooses what to print on one printer: a document, or a published template. Returned by
 * {@link LabelZoomClient#print(String)}.
 *
 * <p>The printer's configuration picks the output language, and LabelZoom converts the document to
 * it, so a PDF prints on a ZPL printer. There are {@code from*} methods only for the formats print
 * converts. For any other format, convert it to the printer's language with
 * {@link LabelZoomClient#convert()} first, then print the result.
 *
 * <p>As with {@link ConversionRequestBuilder}, a named {@code from*} method returns a stage typed for
 * that format, and the methods that take a {@link SourceFormat} return a
 * {@link DynamicPrintBuilder}.
 */
public final class PrintRequestBuilder {

    private final LabelZoomClient client;
    private final String printerId;

    PrintRequestBuilder(LabelZoomClient client, String printerId) {
        this.client = client;
        this.printerId = printerId;
    }

    /**
     * Prints raw bytes whose format is chosen at run time.
     *
     * @throws LabelZoomValidationException if print can't convert {@code format}, with parameter
     *     {@code source}
     */
    public DynamicPrintBuilder from(SourceFormat format, byte[] body) {
        return new DynamicPrintBuilder(draft(PrintWriters.requirePrintable(format), body));
    }

    /** Prints text, encoded as UTF-8, whose format is chosen at run time. */
    public DynamicPrintBuilder from(SourceFormat format, String body) {
        return from(format, ConversionRequestBuilder.utf8(body));
    }

    /**
     * Reads a stream to completion and prints it. Buffered rather than streamed, so a retried request
     * can send the same body again.
     */
    public DynamicPrintBuilder from(SourceFormat format, InputStream body) {
        return from(format, ConversionRequestBuilder.readAll(body));
    }

    /** Reads a file from disk and prints it. */
    public DynamicPrintBuilder fromFile(SourceFormat format, Path path) {
        if (path == null) {
            throw new LabelZoomValidationException("path", "Path cannot be null.");
        }
        PrintWriters.requirePrintable(format);
        try {
            return from(format, Files.readAllBytes(path));
        } catch (IOException e) {
            throw new UncheckedIOException("Could not read " + path, e);
        }
    }

    /** Prints a ZPL document. */
    public ZplPrintBuilder fromZpl(String zpl) {
        return new ZplPrintBuilder(draft(SourceFormat.ZPL, ConversionRequestBuilder.utf8(zpl)));
    }

    /** Prints an EPL/EPL2 document. */
    public PrinterLanguagePrintBuilder fromEpl(String epl) {
        return printerLanguage(SourceFormat.EPL, epl);
    }

    /** Prints an Intermec IPL document. */
    public PrinterLanguagePrintBuilder fromIpl(String ipl) {
        return printerLanguage(SourceFormat.IPL, ipl);
    }

    /** Prints a TSPL/TSPL2 document. */
    public PrinterLanguagePrintBuilder fromTspl(String tspl) {
        return printerLanguage(SourceFormat.TSPL, tspl);
    }

    /** Prints a Datamax DPL document. */
    public PrinterLanguagePrintBuilder fromDpl(String dpl) {
        return printerLanguage(SourceFormat.DPL, dpl);
    }

    /** Prints a SATO SBPL document. */
    public PrinterLanguagePrintBuilder fromSbpl(String sbpl) {
        return printerLanguage(SourceFormat.SBPL, sbpl);
    }

    /** Prints a Honeywell/Intermec Fingerprint or Direct Protocol document. */
    public PrinterLanguagePrintBuilder fromFingerprint(String fingerprint) {
        return printerLanguage(SourceFormat.FINGERPRINT, fingerprint);
    }

    /** Prints a Zebra CPCL document. */
    public PrinterLanguagePrintBuilder fromCpcl(String cpcl) {
        return printerLanguage(SourceFormat.CPCL, cpcl);
    }

    /** Prints a LabelZoom XML label. */
    public PrintBuilder fromXml(String xml) {
        return new PrintBuilder(draft(SourceFormat.XML, ConversionRequestBuilder.utf8(xml)));
    }

    /** Prints a LabelZoom JSON label. */
    public PrintBuilder fromJson(String json) {
        return new PrintBuilder(draft(SourceFormat.JSON, ConversionRequestBuilder.utf8(json)));
    }

    /** Prints a PDF document. */
    public PdfPrintBuilder fromPdf(byte[] pdf) {
        return new PdfPrintBuilder(draft(SourceFormat.PDF, pdf));
    }

    /** Prints a PDF document. */
    public PdfPrintBuilder fromPdf(InputStream pdf) {
        return fromPdf(ConversionRequestBuilder.readAll(pdf));
    }

    /** Prints a PNG image. */
    public ImagePrintBuilder fromPng(byte[] png) {
        return new ImagePrintBuilder(draft(SourceFormat.PNG, png));
    }

    /** Prints a BMP image. */
    public ImagePrintBuilder fromBmp(byte[] bmp) {
        return new ImagePrintBuilder(draft(SourceFormat.BMP, bmp));
    }

    /** Prints a GIF image. */
    public ImagePrintBuilder fromGif(byte[] gif) {
        return new ImagePrintBuilder(draft(SourceFormat.GIF, gif));
    }

    /** Prints a JPEG image. */
    public ImagePrintBuilder fromJpeg(byte[] jpeg) {
        return new ImagePrintBuilder(draft(SourceFormat.JPEG, jpeg));
    }

    /**
     * Prints a template published from LabelZoom Studio or the web designer. Supply its merge-field
     * values with {@link TemplatePrintBuilder#withData(java.util.Map[])}.
     *
     * @param templateId the template's id, from the Print templates page
     */
    public TemplatePrintBuilder fromTemplate(String templateId) {
        return new TemplatePrintBuilder(
                client, printerId, PrintWriters.requireId(templateId, "templateId"));
    }

    private PrinterLanguagePrintBuilder printerLanguage(SourceFormat format, String body) {
        return new PrinterLanguagePrintBuilder(draft(format, ConversionRequestBuilder.utf8(body)));
    }

    private PrintDraft draft(SourceFormat format, byte[] body) {
        if (body == null) {
            throw new LabelZoomValidationException("body", "The document cannot be null.");
        }
        if (body.length == 0) {
            throw new LabelZoomValidationException(
                    "body", "The document cannot be empty; the API rejects an empty print request.");
        }
        return new PrintDraft(new ConversionDraft(client, format, body, format.mediaType()), printerId);
    }
}
