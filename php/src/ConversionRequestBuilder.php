<?php

declare(strict_types=1);

namespace LabelZoom\Sdk;

use LabelZoom\Sdk\Exception\ValidationException;
use LabelZoom\Sdk\Internal\ConversionDraft;

/**
 * Chooses the source document. Returned by `LabelZoomClient::convert()`.
 *
 * A named `from*` method returns a source stage typed for that format, so only the options that
 * apply to it are offered: `fromPdf()` returns a {@see PdfSourceBuilder} with `withPage()`, which
 * `fromZpl()` doesn't have. The methods that take a {@see SourceFormat} pick the format at run time
 * and return a {@see DynamicSourceBuilder}, which offers every source option. No subclass knows a
 * format: it is fixed here, once, and every option is written by one shared function (contract
 * section 1.2).
 */
final class ConversionRequestBuilder
{
    /** @internal */
    public function __construct(private readonly LabelZoomClient $client)
    {
    }

    /**
     * Uses a string of bytes as the source document.
     *
     * PHP strings are byte strings, so this is equally correct for ZPL text and for the contents
     * of a PDF.
     */
    public function from(SourceFormat $format, string $body): DynamicSourceBuilder
    {
        return new DynamicSourceBuilder($this->draft($format, $body));
    }

    private function draft(SourceFormat $format, string $body): ConversionDraft
    {
        if ($body === '') {
            // The gateway rejects a zero-length body with 400. Catching it here saves a round trip
            // and says something more useful than "Request body is required".
            throw new ValidationException(
                'body',
                'Source body cannot be empty; the API rejects zero-length requests.',
            );
        }

        return new ConversionDraft($this->client, $format, $body, $format->mediaType());
    }

    /** Reads a file from disk and uses it as the source document. */
    public function fromFile(SourceFormat $format, string $path): DynamicSourceBuilder
    {
        $contents = @file_get_contents($path);
        if ($contents === false) {
            throw new ValidationException('path', "Could not read {$path}");
        }

        return $this->from($format, $contents);
    }

    /**
     * Reads a stream to completion and uses it as the source document.
     *
     * Buffered rather than streamed, because a retried request has to send the same body again and
     * a consumed stream cannot be replayed.
     *
     * @param resource $stream
     */
    public function fromStream(SourceFormat $format, $stream): DynamicSourceBuilder
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

    /**
     * Uses a base64-encoded document as the source, sent as `text/plain`.
     *
     * The API accepts PDF and image sources either as raw bytes with their own media type or as
     * base64 text. Prefer {@see self::from()}; this exists for callers whose transport has already
     * base64-encoded the payload.
     */
    public function fromBase64Text(SourceFormat $format, string $base64): DynamicSourceBuilder
    {
        if ($base64 === '') {
            throw new ValidationException('body', 'Base64 body cannot be empty.');
        }

        return new DynamicSourceBuilder(new ConversionDraft($this->client, $format, $base64, 'text/plain'));
    }

    /** Converts from ZPL. */
    public function fromZpl(string $zpl): ZplSourceBuilder
    {
        return new ZplSourceBuilder($this->draft(SourceFormat::Zpl, $zpl));
    }

    /** Converts from EPL/EPL2. */
    public function fromEpl(string $epl): PrinterLanguageSourceBuilder
    {
        return new PrinterLanguageSourceBuilder($this->draft(SourceFormat::Epl, $epl));
    }

    /** Converts from Intermec IPL. */
    public function fromIpl(string $ipl): PrinterLanguageSourceBuilder
    {
        return new PrinterLanguageSourceBuilder($this->draft(SourceFormat::Ipl, $ipl));
    }

    /** Converts from TSPL/TSPL2. */
    public function fromTspl(string $tspl): PrinterLanguageSourceBuilder
    {
        return new PrinterLanguageSourceBuilder($this->draft(SourceFormat::Tspl, $tspl));
    }

    /** Converts from DPL. */
    public function fromDpl(string $dpl): PrinterLanguageSourceBuilder
    {
        return new PrinterLanguageSourceBuilder($this->draft(SourceFormat::Dpl, $dpl));
    }

    /** Converts from SATO SBPL. */
    public function fromSbpl(string $sbpl): PrinterLanguageSourceBuilder
    {
        return new PrinterLanguageSourceBuilder($this->draft(SourceFormat::Sbpl, $sbpl));
    }

    /** Converts from LabelZoom XML. */
    public function fromXml(string $xml): SourceBuilder
    {
        return new SourceBuilder($this->draft(SourceFormat::Xml, $xml));
    }

    /** Converts from LabelZoom JSON. */
    public function fromJson(string $json): SourceBuilder
    {
        return new SourceBuilder($this->draft(SourceFormat::Json, $json));
    }

    /** Converts from a PDF document. */
    public function fromPdf(string $pdf): PdfSourceBuilder
    {
        return new PdfSourceBuilder($this->draft(SourceFormat::Pdf, $pdf));
    }

    /** Converts from a PNG image. */
    public function fromPng(string $png): ImageSourceBuilder
    {
        return new ImageSourceBuilder($this->draft(SourceFormat::Png, $png));
    }

    /** Converts from a BMP image. */
    public function fromBmp(string $bmp): ImageSourceBuilder
    {
        return new ImageSourceBuilder($this->draft(SourceFormat::Bmp, $bmp));
    }

    /** Converts from a GIF image. */
    public function fromGif(string $gif): ImageSourceBuilder
    {
        return new ImageSourceBuilder($this->draft(SourceFormat::Gif, $gif));
    }

    /** Converts from a JPEG image. */
    public function fromJpeg(string $jpeg): ImageSourceBuilder
    {
        return new ImageSourceBuilder($this->draft(SourceFormat::Jpeg, $jpeg));
    }

    /**
     * Has the *server* fetch a URL and convert whatever it finds there.
     *
     * This hands a caller-supplied URL to a server-side fetch. Validate it first if it came from
     * untrusted input.
     */
    public function fromUrl(string $url): DynamicSourceBuilder
    {
        if (trim($url) === '') {
            throw new ValidationException('body', 'URL cannot be empty.');
        }

        return $this->from(SourceFormat::Url, $url);
    }
}
