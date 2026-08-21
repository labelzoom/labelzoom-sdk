<?php

declare(strict_types=1);

namespace LabelZoom\Sdk;

/**
 * The outcome of a successful conversion.
 *
 * {@see self::getBytes()} is authoritative. PDF, PNG, BMP, GIF and JPEG targets are binary, so a
 * result that offered only a string would silently corrupt five of the eight targets (rule D1).
 */
final class ConversionResult
{
    public function __construct(
        private readonly string $bytes,
        private readonly ?string $contentType,
        private readonly int $status,
        private readonly ?string $requestId,
    ) {
    }

    /**
     * The converted document, exactly as the server sent it.
     *
     * A PHP string is a byte string; this is binary-safe.
     */
    public function getBytes(): string
    {
        return $this->bytes;
    }

    /** The response `Content-Type`, including any charset parameter. */
    public function getContentType(): ?string
    {
        return $this->contentType;
    }

    /** The HTTP status code. Always 2xx here. */
    public function getStatus(): int
    {
        return $this->status;
    }

    /**
     * The `X-LZ-Request-Id` response header, or null if the server did not send one.
     *
     * Quote it when contacting LabelZoom support.
     */
    public function getRequestId(): ?string
    {
        return $this->requestId;
    }

    /**
     * {@see self::getBytes()} decoded to a UTF-8 string using the response charset.
     *
     * Meaningful for the ZPL, XML and JSON targets. Decoding a PNG will succeed and produce
     * nonsense — use {@see self::getBytes()} for binary targets.
     */
    public function getText(): string
    {
        $charset = $this->charset();
        if ($charset === null || $this->isUtf8($charset)) {
            return $this->bytes;
        }

        $converted = @mb_convert_encoding($this->bytes, 'UTF-8', $charset);

        // An unrecognized charset is not worth failing an otherwise good conversion over; the
        // raw bytes are still the document.
        return $converted === false ? $this->bytes : $converted;
    }

    /** Writes the converted document to a file, overwriting any existing one. */
    public function save(string $path): void
    {
        if (file_put_contents($path, $this->bytes) === false) {
            throw new \RuntimeException("Could not write the conversion result to {$path}");
        }
    }

    /** The charset named in `Content-Type`, or null when none was declared. */
    private function charset(): ?string
    {
        if ($this->contentType === null) {
            return null;
        }
        if (preg_match('/charset=([^;\s]+)/i', $this->contentType, $matches) !== 1) {
            return null;
        }

        return trim($matches[1], "\"'");
    }

    private function isUtf8(string $charset): bool
    {
        return in_array(strtolower(str_replace('-', '', $charset)), ['utf8', 'usascii', 'ascii'], true);
    }
}
