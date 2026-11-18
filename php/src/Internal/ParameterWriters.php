<?php

declare(strict_types=1);

namespace LabelZoom\Sdk\Internal;

use LabelZoom\Sdk\ColorMode;
use LabelZoom\Sdk\Exception\ValidationException;
use LabelZoom\Sdk\PdfConversionMode;
use LabelZoom\Sdk\ZplImageCompression;

/**
 * The validation and wire mapping for every option, written once.
 *
 * Several builders expose the same option: `PdfSourceBuilder::withPage()` and
 * `DynamicSourceBuilder::withPdfPage()` both write `pdf.pageNumber`. Each public method is a
 * one-line call into here, so the builders cannot drift apart on validation or on the JSON path.
 *
 * @internal
 */
final class ParameterWriters
{
    // Contract C8: sourceDpi from the source stage, targetDpi from the target stage, never dpi.
    public static function sourceDpi(ConversionDraft $draft, int $dpi): void
    {
        $draft->params['sourceDpi'] = self::positiveDpi($dpi);
    }

    public static function targetDpi(ConversionDraft $draft, int $dpi): void
    {
        $draft->params['targetDpi'] = self::positiveDpi($dpi);
    }

    private static function positiveDpi(int $dpi): int
    {
        if ($dpi <= 0) {
            throw new ValidationException('dpi', 'DPI must be greater than zero.');
        }

        return $dpi;
    }

    public static function labelSize(ConversionDraft $draft, float $widthInches, float $heightInches): void
    {
        if ($widthInches <= 0 || $heightInches <= 0) {
            throw new ValidationException('label', 'Label width and height must be greater than zero.');
        }
        $draft->params['label'] = ['width' => $widthInches, 'height' => $heightInches];
    }

    // No local range check: contract section 5 leaves labelIndex limits to the server.
    public static function labelIndex(ConversionDraft $draft, int $zeroBasedIndex): void
    {
        $draft->params['labelIndex'] = $zeroBasedIndex;
    }

    /**
     * @param array<string, mixed>|list<array<string, mixed>> $records
     */
    public static function data(ConversionDraft $draft, array $records): void
    {
        $draft->params['data'] = self::records($records);
    }

    /**
     * Validates data records and puts them in their wire shape: a list of JSON objects.
     *
     * Shared by the source stages' `withData()` and the template print's, so a record that is
     * rejected in one is rejected in the other (rule C3).
     *
     * @param array<string, mixed>|list<array<string, mixed>> $records
     *
     * @return list<object>
     */
    public static function records(array $records): array
    {
        if ($records === []) {
            throw new ValidationException('data', 'Provide at least one data record, or omit this call entirely.');
        }

        // A bare associative array is one record, not a list of them. array_is_list is the only
        // reliable discriminator: PHP models both shapes with the same type.
        $list = array_is_list($records) ? $records : [$records];

        $normalized = [];
        foreach ($list as $index => $record) {
            if (!is_array($record)) {
                throw new ValidationException(
                    'data',
                    "data[{$index}] is not an object; every entry must be a key/value map.",
                );
            }
            // Cast so an empty record encodes as {} rather than [] -- json_encode cannot tell an
            // empty map from an empty list.
            $normalized[] = (object) $record;
        }

        return $normalized;
    }

    public static function pdfPage(ConversionDraft $draft, int $zeroBasedPageNumber): void
    {
        if ($zeroBasedPageNumber < 0) {
            throw new ValidationException(
                'pdf',
                'Page number is 0-based and cannot be negative; 0 selects the first page.',
            );
        }
        $draft->nested('pdf', 'pageNumber', $zeroBasedPageNumber);
    }

    public static function pdfConversionMode(ConversionDraft $draft, PdfConversionMode $mode): void
    {
        $draft->nested('pdf', 'conversionMode', $mode->value);
    }

    /**
     * Any array of strings is accepted, not only a list. A caller who built the array with
     * array_filter leaves gaps in the keys, and a gapped array encodes as a JSON *object* -- which
     * the server reads as no commands at all rather than as an error, so the renumbering below is
     * load-bearing.
     *
     * @param array<array-key, string> $commands
     */
    public static function zplCommandsToIgnore(ConversionDraft $draft, array $commands): void
    {
        if ($commands === []) {
            throw new ValidationException('zpl', 'Provide at least one command, or omit this call entirely.');
        }
        $draft->nested('zpl', 'commandsToIgnore', array_values($commands));
    }

    public static function scaling(ConversionDraft $draft, float $percent): void
    {
        if ($percent <= 0) {
            throw new ValidationException('scaling', 'Scaling must be greater than zero.');
        }
        $draft->params['scaling'] = $percent;
    }

    public static function position(ConversionDraft $draft, int $x, int $y): void
    {
        $draft->params['position'] = ['x' => $x, 'y' => $y];
    }

    public static function darkness(ConversionDraft $draft, int $darkness): void
    {
        if ($darkness < 0 || $darkness > 100) {
            throw new ValidationException(
                'darkness',
                "Darkness must be between 0 and 100, but was {$darkness}.",
            );
        }
        $draft->params['darkness'] = $darkness;
    }

    public static function colorMode(ConversionDraft $draft, ColorMode $mode): void
    {
        $draft->params['colorMode'] = $mode->value;
    }

    /**
     * Rejected locally when it is not a multiple of 90 -- the server would 400, and this is
     * unambiguously a caller bug (rule C6).
     */
    public static function rotation(ConversionDraft $draft, int $rotation): void
    {
        if ($rotation % 90 !== 0) {
            throw new ValidationException(
                'rotation',
                "Rotation must be a multiple of 90 degrees, but was {$rotation}.",
            );
        }
        $draft->params['rotation'] = $rotation;
    }

    public static function dialect(ConversionDraft $draft, string $dialect): void
    {
        if (trim($dialect) === '') {
            throw new ValidationException('dialect', 'Dialect cannot be empty.');
        }
        $draft->params['dialect'] = $dialect;
    }

    public static function zplImageCompression(ConversionDraft $draft, ZplImageCompression $compression): void
    {
        $draft->nested('zpl', 'imageCompression', $compression->value);
    }

    public static function zplCollapseCopies(ConversionDraft $draft, bool $collapse): void
    {
        $draft->nested('zpl', 'collapseCopies', $collapse);
    }

    public static function parameter(ConversionDraft $draft, string $key, mixed $value): void
    {
        if (trim($key) === '') {
            throw new ValidationException('params', 'Parameter key cannot be empty.');
        }
        $draft->params[$key] = $value;
    }

    public static function rawQueryParameter(ConversionDraft $draft, string $key, string $value): void
    {
        if (trim($key) === '') {
            throw new ValidationException('params', 'Query parameter key cannot be empty.');
        }
        $draft->rawQuery[$key] = $value;
    }
}
