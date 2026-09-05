<?php

declare(strict_types=1);

namespace LabelZoom\Sdk;

use LabelZoom\Sdk\Exception\ValidationException;

/**
 * Chooses the source document.
 *
 * There is one source builder and one target builder for all 13 x 8 format combinations, not a
 * class per format. The named `from*` methods are one-line delegations to
 * {@see self::from()}: they exist for discoverability and, holding no logic of their own, cannot
 * drift away from the format table.
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
    public function from(SourceFormat $format, string $body): ConversionSourceBuilder
    {
        if ($body === '') {
            // The gateway rejects a zero-length body with 400. Catching it here saves a round trip
            // and says something more useful than "Request body is required".
            throw new ValidationException(
                'body',
                'Source body cannot be empty; the API rejects zero-length requests.',
            );
        }

        return new ConversionSourceBuilder($this->client, $format, $body, $format->mediaType());
    }

    /** Reads a file from disk and uses it as the source document. */
    public function fromFile(SourceFormat $format, string $path): ConversionSourceBuilder
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
    public function fromStream(SourceFormat $format, $stream): ConversionSourceBuilder
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
    public function fromBase64Text(SourceFormat $format, string $base64): ConversionSourceBuilder
    {
        if ($base64 === '') {
            throw new ValidationException('body', 'Base64 body cannot be empty.');
        }

        return new ConversionSourceBuilder($this->client, $format, $base64, 'text/plain');
    }

    /** Converts from ZPL. */
    public function fromZpl(string $zpl): ConversionSourceBuilder
    {
        return $this->from(SourceFormat::Zpl, $zpl);
    }

    /** Converts from EPL/EPL2. */
    public function fromEpl(string $epl): ConversionSourceBuilder
    {
        return $this->from(SourceFormat::Epl, $epl);
    }

    /** Converts from Intermec IPL. */
    public function fromIpl(string $ipl): ConversionSourceBuilder
    {
        return $this->from(SourceFormat::Ipl, $ipl);
    }

    /** Converts from TSPL/TSPL2. */
    public function fromTspl(string $tspl): ConversionSourceBuilder
    {
        return $this->from(SourceFormat::Tspl, $tspl);
    }

    /** Converts from DPL. */
    public function fromDpl(string $dpl): ConversionSourceBuilder
    {
        return $this->from(SourceFormat::Dpl, $dpl);
    }

    /** Converts from SATO SBPL. */
    public function fromSbpl(string $sbpl): ConversionSourceBuilder
    {
        return $this->from(SourceFormat::Sbpl, $sbpl);
    }

    /** Converts from LabelZoom XML. */
    public function fromXml(string $xml): ConversionSourceBuilder
    {
        return $this->from(SourceFormat::Xml, $xml);
    }

    /** Converts from LabelZoom JSON. */
    public function fromJson(string $json): ConversionSourceBuilder
    {
        return $this->from(SourceFormat::Json, $json);
    }

    /** Converts from a PDF document. */
    public function fromPdf(string $pdf): ConversionSourceBuilder
    {
        return $this->from(SourceFormat::Pdf, $pdf);
    }

    /** Converts from a PNG image. */
    public function fromPng(string $png): ConversionSourceBuilder
    {
        return $this->from(SourceFormat::Png, $png);
    }

    /** Converts from a BMP image. */
    public function fromBmp(string $bmp): ConversionSourceBuilder
    {
        return $this->from(SourceFormat::Bmp, $bmp);
    }

    /** Converts from a GIF image. */
    public function fromGif(string $gif): ConversionSourceBuilder
    {
        return $this->from(SourceFormat::Gif, $gif);
    }

    /** Converts from a JPEG image. */
    public function fromJpeg(string $jpeg): ConversionSourceBuilder
    {
        return $this->from(SourceFormat::Jpeg, $jpeg);
    }

    /**
     * Has the *server* fetch a URL and convert whatever it finds there.
     *
     * This hands a caller-supplied URL to a server-side fetch. Validate it first if it came from
     * untrusted input.
     */
    public function fromUrl(string $url): ConversionSourceBuilder
    {
        if (trim($url) === '') {
            throw new ValidationException('body', 'URL cannot be empty.');
        }

        return $this->from(SourceFormat::Url, $url);
    }
}
