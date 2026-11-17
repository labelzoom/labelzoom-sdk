<?php

declare(strict_types=1);

namespace LabelZoom\Sdk\Internal;

use LabelZoom\Sdk\Exception\ValidationException;
use LabelZoom\Sdk\SourceFormat;

/**
 * The print options' validation and wire spelling, written once.
 *
 * The print stages call into these the way the convert stages call into {@see ParameterWriters},
 * so a document print and a template print can't validate `copies` differently. Source options go
 * through {@see ParameterWriters} itself.
 *
 * @internal
 */
final class PrintWriters
{
    public const JSON_MEDIA_TYPE = 'application/json';

    public const MIN_COPIES = 1;

    public const MAX_COPIES = 9999;

    /**
     * The sources the print endpoint converts to the printer's language (contract P2). It sends
     * any other format to the printer as is, so the SDK refuses it.
     */
    private const PRINTABLE = [
        SourceFormat::Zpl,
        SourceFormat::Epl,
        SourceFormat::Ipl,
        SourceFormat::Tspl,
        SourceFormat::Dpl,
        SourceFormat::Sbpl,
        SourceFormat::Fingerprint,
        SourceFormat::Cpcl,
        SourceFormat::Xml,
        SourceFormat::Json,
        SourceFormat::Pdf,
        SourceFormat::Png,
        SourceFormat::Bmp,
        SourceFormat::Gif,
        SourceFormat::Jpeg,
        SourceFormat::Jpg,
    ];

    public static function requirePrintable(SourceFormat $format): SourceFormat
    {
        if (!in_array($format, self::PRINTABLE, true)) {
            throw new ValidationException(
                'source',
                "{$format->value} can't be printed directly: print converts only ZPL, EPL, IPL, TSPL, DPL, "
                . 'SBPL, Fingerprint, CPCL, LabelZoom XML and JSON, PDF and images. Convert it to the '
                . "printer's language with convert() first, then print the result.",
            );
        }

        return $format;
    }

    /** An id that goes into the path. An empty one would address a different endpoint. */
    public static function requireId(string $id, string $parameter): string
    {
        if (trim($id) === '') {
            throw new ValidationException($parameter, "{$parameter} cannot be empty.");
        }

        return $id;
    }

    public static function copies(int $copies): int
    {
        if ($copies < self::MIN_COPIES || $copies > self::MAX_COPIES) {
            throw new ValidationException(
                'copies',
                'copies must be a whole number from ' . self::MIN_COPIES . ' to ' . self::MAX_COPIES
                . ", but was {$copies}.",
            );
        }

        return $copies;
    }

    public static function idempotencyKey(string $key): string
    {
        if (trim($key) === '') {
            throw new ValidationException(
                'idempotencyKey',
                'An Idempotency-Key cannot be blank. Omit withIdempotencyKey() to have the SDK generate one.',
            );
        }

        return $key;
    }

    /**
     * The key for one call: the caller's, or a new random UUID v4. The caller fixes it before the
     * first attempt, so every retry of that call sends the same one (contract P4).
     */
    public static function keyForCall(?string $callerKey): string
    {
        return $callerKey ?? self::uuid4();
    }

    /** Percent-encodes an id as one path segment (contract P1). */
    public static function segment(string $id): string
    {
        return rawurlencode($id);
    }

    /**
     * A template print's body: one record as that object, several as an array, and none as `{}`
     * (contract P3).
     *
     * @param list<object> $records
     */
    public static function templateBody(array $records): string
    {
        if ($records === []) {
            return '{}';
        }
        if (count($records) === 1) {
            return Json::encode($records[0]);
        }

        return Json::encode($records);
    }

    /** A random, RFC 4122 version 4 UUID in lowercase. */
    private static function uuid4(): string
    {
        $bytes = random_bytes(16);
        $bytes[6] = chr((ord($bytes[6]) & 0x0F) | 0x40);
        $bytes[8] = chr((ord($bytes[8]) & 0x3F) | 0x80);
        $hex = bin2hex($bytes);

        return substr($hex, 0, 8) . '-' . substr($hex, 8, 4) . '-' . substr($hex, 12, 4) . '-'
            . substr($hex, 16, 4) . '-' . substr($hex, 20, 12);
    }
}
