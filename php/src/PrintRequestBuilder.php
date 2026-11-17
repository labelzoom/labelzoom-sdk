<?php

declare(strict_types=1);

namespace LabelZoom\Sdk;

use LabelZoom\Sdk\Exception\ValidationException;
use LabelZoom\Sdk\Internal\ConversionDraft;
use LabelZoom\Sdk\Internal\PrintWriters;

/**
 * Chooses what to print: a document, or a published template. Returned by
 * `LabelZoomClient::print()`.
 *
 * The printer's configuration picks the output language, and LabelZoom converts the document to
 * it, so a PDF prints on a ZPL printer. A named `from*` method returns the print twin of the convert
 * source stage for that format, with the same source options: `fromPdf()` returns a
 * {@see PdfPrintBuilder} with `withPage()`. There are `from*` methods only for the formats print
 * converts. To print any other format, convert it to the printer's language first and print the
 * result.
 */
final class PrintRequestBuilder
{
    /** @internal */
    public function __construct(
        private readonly LabelZoomClient $client,
        private readonly string $printerId,
    ) {
    }

    /**
     * Prints a document whose format is chosen at run time.
     *
     * PHP strings are byte strings, so this is equally correct for ZPL text and for the contents of
     * a PDF.
     *
     * @throws ValidationException if the format can't be printed directly, or the body is empty
     */
    public function from(SourceFormat $format, string $body): DynamicPrintBuilder
    {
        return new DynamicPrintBuilder(
            $this->draft(PrintWriters::requirePrintable($format), $body),
            $this->printerId,
        );
    }

    /** Reads a file from disk and prints it. */
    public function fromFile(SourceFormat $format, string $path): DynamicPrintBuilder
    {
        $contents = @file_get_contents($path);
        if ($contents === false) {
            throw new ValidationException('path', "Could not read {$path}");
        }

        return $this->from($format, $contents);
    }

    /**
     * Reads a stream to completion and prints it.
     *
     * Buffered rather than streamed, because a retried request has to send the same body again and
     * a consumed stream cannot be replayed.
     *
     * @param resource $stream
     */
    public function fromStream(SourceFormat $format, $stream): DynamicPrintBuilder
    {
        if (!is_resource($stream)) {
            throw new ValidationException('body', 'Source stream is not a valid resource.');
        }
        $contents = stream_get_contents($stream);
        if ($contents === false) {
            throw new ValidationException('body', 'Could not read the source stream.');
        }

        return $this->from($format, $contents);
    }

    /** Prints a ZPL document. */
    public function fromZpl(string $zpl): ZplPrintBuilder
    {
        return new ZplPrintBuilder($this->draft(SourceFormat::Zpl, $zpl), $this->printerId);
    }

    /** Prints an EPL/EPL2 document. */
    public function fromEpl(string $epl): PrinterLanguagePrintBuilder
    {
        return $this->printerLanguage(SourceFormat::Epl, $epl);
    }

    /** Prints an Intermec IPL document. */
    public function fromIpl(string $ipl): PrinterLanguagePrintBuilder
    {
        return $this->printerLanguage(SourceFormat::Ipl, $ipl);
    }

    /** Prints a TSPL/TSPL2 document. */
    public function fromTspl(string $tspl): PrinterLanguagePrintBuilder
    {
        return $this->printerLanguage(SourceFormat::Tspl, $tspl);
    }

    /** Prints a Datamax DPL document. */
    public function fromDpl(string $dpl): PrinterLanguagePrintBuilder
    {
        return $this->printerLanguage(SourceFormat::Dpl, $dpl);
    }

    /** Prints a SATO SBPL document. */
    public function fromSbpl(string $sbpl): PrinterLanguagePrintBuilder
    {
        return $this->printerLanguage(SourceFormat::Sbpl, $sbpl);
    }

    /** Prints a Honeywell/Intermec Fingerprint or Direct Protocol document. */
    public function fromFingerprint(string $fingerprint): PrinterLanguagePrintBuilder
    {
        return $this->printerLanguage(SourceFormat::Fingerprint, $fingerprint);
    }

    /** Prints a Zebra CPCL document. */
    public function fromCpcl(string $cpcl): PrinterLanguagePrintBuilder
    {
        return $this->printerLanguage(SourceFormat::Cpcl, $cpcl);
    }

    /** Prints a LabelZoom XML label. */
    public function fromXml(string $xml): PrintBuilder
    {
        return new PrintBuilder($this->draft(SourceFormat::Xml, $xml), $this->printerId);
    }

    /** Prints a LabelZoom JSON label. */
    public function fromJson(string $json): PrintBuilder
    {
        return new PrintBuilder($this->draft(SourceFormat::Json, $json), $this->printerId);
    }

    /** Prints a PDF document. */
    public function fromPdf(string $pdf): PdfPrintBuilder
    {
        return new PdfPrintBuilder($this->draft(SourceFormat::Pdf, $pdf), $this->printerId);
    }

    /** Prints a PNG image. */
    public function fromPng(string $png): ImagePrintBuilder
    {
        return $this->image(SourceFormat::Png, $png);
    }

    /** Prints a BMP image. */
    public function fromBmp(string $bmp): ImagePrintBuilder
    {
        return $this->image(SourceFormat::Bmp, $bmp);
    }

    /** Prints a GIF image. */
    public function fromGif(string $gif): ImagePrintBuilder
    {
        return $this->image(SourceFormat::Gif, $gif);
    }

    /** Prints a JPEG image. */
    public function fromJpeg(string $jpeg): ImagePrintBuilder
    {
        return $this->image(SourceFormat::Jpeg, $jpeg);
    }

    /**
     * Prints a template published from LabelZoom Studio or the web designer. Supply its
     * merge-field values with {@see TemplatePrintBuilder::withData()}.
     *
     * @param string $templateId the template's id, from the Print templates page
     *
     * @throws ValidationException if the template id is empty
     */
    public function fromTemplate(string $templateId): TemplatePrintBuilder
    {
        return new TemplatePrintBuilder(
            $this->client,
            $this->printerId,
            PrintWriters::requireId($templateId, 'templateId'),
        );
    }

    private function printerLanguage(SourceFormat $format, string $body): PrinterLanguagePrintBuilder
    {
        return new PrinterLanguagePrintBuilder($this->draft($format, $body), $this->printerId);
    }

    private function image(SourceFormat $format, string $body): ImagePrintBuilder
    {
        return new ImagePrintBuilder($this->draft($format, $body), $this->printerId);
    }

    private function draft(SourceFormat $format, string $body): ConversionDraft
    {
        if ($body === '') {
            throw new ValidationException(
                'body',
                'The document cannot be empty; the API rejects an empty print request.',
            );
        }

        // The same draft the convert source stage writes into, so every source option goes
        // through the one shared writer and serializes exactly as it does for convert (rule P2).
        return new ConversionDraft($this->client, $format, $body, $format->mediaType());
    }
}
