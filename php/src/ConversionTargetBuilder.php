<?php

declare(strict_types=1);

namespace LabelZoom\Sdk;

use LabelZoom\Sdk\Exception\ValidationException;

/**
 * Configures and executes a conversion.
 *
 * Every `with*` method records its value and is actually sent. Only options you set are
 * serialized — the SDK never fills in a client-side default, so a change to a server default
 * reaches you without an SDK upgrade (rule C1).
 */
final class ConversionTargetBuilder
{
    /** @var array<string, mixed> */
    private array $params = [];

    /** @var array<string, string> */
    private array $rawQuery = [];

    /** @internal */
    public function __construct(
        private readonly LabelZoomClient $client,
        private readonly SourceFormat $source,
        private readonly TargetFormat $target,
        private readonly string $body,
        private readonly string $contentType,
    ) {
    }

    /** Output resolution in dots per inch. The server default is 203. */
    public function withDpi(int $dpi): self
    {
        if ($dpi <= 0) {
            throw new ValidationException('dpi', 'DPI must be greater than zero.');
        }
        $this->params['dpi'] = $dpi;

        return $this;
    }

    /**
     * Rotation in degrees clockwise: 0, 90, 180 or 270. The server default is 0.
     *
     * Rejected locally when it is not a multiple of 90 — the server would 400, and this is
     * unambiguously a caller bug (rule C6).
     */
    public function withRotation(int $rotation): self
    {
        if ($rotation % 90 !== 0) {
            throw new ValidationException(
                'rotation',
                "Rotation must be a multiple of 90 degrees, but was {$rotation}.",
            );
        }
        $this->params['rotation'] = $rotation;

        return $this;
    }

    /** Scaling as a percentage. The server default is 100. */
    public function withScaling(float $percent): self
    {
        if ($percent <= 0) {
            throw new ValidationException('scaling', 'Scaling must be greater than zero.');
        }
        $this->params['scaling'] = $percent;

        return $this;
    }

    /** Color handling. The server default is {@see ColorMode::Grayscale}. */
    public function withColorMode(ColorMode $mode): self
    {
        $this->params['colorMode'] = $mode->value;

        return $this;
    }

    /** Luminance threshold from 0 to 100 used when reducing color depth. Server default 70. */
    public function withDarkness(int $darkness): self
    {
        if ($darkness < 0 || $darkness > 100) {
            throw new ValidationException(
                'darkness',
                "Darkness must be between 0 and 100, but was {$darkness}.",
            );
        }
        $this->params['darkness'] = $darkness;

        return $this;
    }

    /** Pixel offset of the top-left corner of the extracted region. */
    public function withPosition(int $x, int $y): self
    {
        $this->params['position'] = ['x' => $x, 'y' => $y];

        return $this;
    }

    /** Requests a watermark. Output is watermarked regardless on the anonymous free tier. */
    public function withWatermark(bool $watermark = true): self
    {
        $this->params['watermark'] = $watermark;

        return $this;
    }

    /**
     * Selects a printer dialect, for example `moca` for Blue Yonder WMS.
     *
     * Requires a paid license; without one the request fails with a 403 whose
     * {@see Exception\ForbiddenException::isPaidFeature()} is set.
     */
    public function withDialect(string $dialect): self
    {
        if (trim($dialect) === '') {
            throw new ValidationException('dialect', 'Dialect cannot be empty.');
        }
        $this->params['dialect'] = $dialect;

        return $this;
    }

    /**
     * Label dimensions **in inches**, overriding whatever the source document implies.
     *
     * Inches — not dots, not millimeters. This and the 0-based page number in
     * {@see self::withPdfPage()} are the two most misread parameters in the API.
     */
    public function withLabelSize(float $widthInches, float $heightInches): self
    {
        if ($widthInches <= 0 || $heightInches <= 0) {
            throw new ValidationException('label', 'Label width and height must be greater than zero.');
        }
        $this->params['label'] = ['width' => $widthInches, 'height' => $heightInches];

        return $this;
    }

    /** How a source PDF is interpreted. The server default is {@see PdfConversionMode::Image}. */
    public function withPdfConversionMode(PdfConversionMode $mode): self
    {
        $this->nested('pdf', 'conversionMode', $mode->value);

        return $this;
    }

    /**
     * Converts a single page of a source PDF, identified by a **0-based** index.
     *
     * Omit this call entirely to convert every page. 0 selects the first page.
     */
    public function withPdfPage(int $zeroBasedPageNumber): self
    {
        if ($zeroBasedPageNumber < 0) {
            throw new ValidationException(
                'pdf',
                'Page number is 0-based and cannot be negative; 0 selects the first page.',
            );
        }
        $this->nested('pdf', 'pageNumber', $zeroBasedPageNumber);

        return $this;
    }

    /**
     * ZPL commands the parser should skip, for example `^PQ`.
     *
     * Any array of strings is accepted, not only a list. A caller who built the array with
     * array_filter leaves gaps in the keys, and a gapped array encodes as a JSON *object* —
     * which the server reads as no commands at all rather than as an error, so the
     * renumbering below is load-bearing.
     *
     * @param array<array-key, string> $commands
     */
    public function withZplCommandsToIgnore(array $commands): self
    {
        if ($commands === []) {
            throw new ValidationException('zpl', 'Provide at least one command, or omit this call entirely.');
        }
        $this->nested('zpl', 'commandsToIgnore', array_values($commands));

        return $this;
    }

    /** Image compression used when writing ZPL. The server default is {@see ZplImageCompression::Z64}. */
    public function withZplImageCompression(ZplImageCompression $compression): self
    {
        $this->nested('zpl', 'imageCompression', $compression->value);

        return $this;
    }

    /**
     * Image or PDF to ZPL: sends each run of identical labels as one label with a `^PQ` print
     * quantity. The server default is `false`.
     */
    public function withZplCollapseCopies(bool $collapse = true): self
    {
        $this->nested('zpl', 'collapseCopies', $collapse);

        return $this;
    }

    /**
     * Image or PDF to ZPL, IMAGE mode: puts `$across` labels side by side in each printed row, for
     * multi-column roll stock.
     *
     * The gap is **in inches** — not dots.
     */
    public function withLayout(int $across, float $gapInches): self
    {
        $this->nested('layout', 'across', $across);
        $this->nested('layout', 'gap', $gapInches);

        return $this;
    }

    /** How pages fill the columns of a row. The server default is {@see LayoutFill::Sequential}. */
    public function withLayoutFill(LayoutFill $fill): self
    {
        $this->nested('layout', 'fill', $fill->value);

        return $this;
    }

    /**
     * Image or PDF to ZPL, IMAGE mode: cuts each page into `$rows` × `$columns` labels, read left
     * to right and then down. 1 × 1 is no split.
     */
    public function withSplit(int $rows, int $columns): self
    {
        $this->nested('split', 'rows', $rows);
        $this->nested('split', 'columns', $columns);

        return $this;
    }

    /** The size of one label on the source page, **in inches**. Omit this to divide the page equally. */
    public function withSplitLabelSize(float $widthInches, float $heightInches): self
    {
        $this->nested('split', 'width', $widthInches);
        $this->nested('split', 'height', $heightInches);

        return $this;
    }

    /** Distance from the page's left and top edges to the first label, **in inches**. */
    public function withSplitMargins(float $leftInches, float $topInches): self
    {
        $this->nested('split', 'left', $leftInches);
        $this->nested('split', 'top', $topInches);

        return $this;
    }

    /** Space between two columns and between two rows of labels on the page, **in inches**. */
    public function withSplitGaps(float $columnGapInches, float $rowGapInches): self
    {
        $this->nested('split', 'columnGap', $columnGapInches);
        $this->nested('split', 'rowGap', $rowGapInches);

        return $this;
    }

    /** Leaves out labels with nothing printable, such as the unused end of a partly filled sheet. */
    public function withSplitSkipBlank(bool $skipBlank = true): self
    {
        $this->nested('split', 'skipBlank', $skipBlank);

        return $this;
    }

    /**
     * Converts only one label of a multi-label input, on every plan, identified by a **0-based**
     * index.
     *
     * For image or PDF to ZPL it counts the labels after {@see self::withSplit()}, page by page.
     */
    public function withLabelIndex(int $zeroBasedIndex): self
    {
        $this->params['labelIndex'] = $zeroBasedIndex;

        return $this;
    }

    /**
     * Supplies data to fill the label's variable fields. **Each record produces one label.**
     *
     * A single record may be passed on its own; it is wrapped into a one-element array rather
     * than rejected (rule C3).
     *
     * @param array<string, mixed>|list<array<string, mixed>> $records
     */
    public function withData(array $records): self
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

        $this->params['data'] = $normalized;

        return $this;
    }

    /**
     * Sets a parameter the SDK does not model yet.
     *
     * Unknown keys are ignored by the server, so this is a safe forward-compatibility hatch.
     */
    public function withParameter(string $key, mixed $value): self
    {
        if (trim($key) === '') {
            throw new ValidationException('params', 'Parameter key cannot be empty.');
        }
        $this->params[$key] = $value;

        return $this;
    }

    /** Adds a raw query-string parameter alongside `params`. */
    public function withRawQueryParameter(string $key, string $value): self
    {
        if (trim($key) === '') {
            throw new ValidationException('params', 'Query parameter key cannot be empty.');
        }
        $this->rawQuery[$key] = $value;

        return $this;
    }

    /**
     * Executes the conversion.
     *
     * @throws Exception\LabelZoomException if the API returns a non-2xx response
     * @throws Exception\TransportException if no response was ever produced
     */
    public function execute(): ConversionResult
    {
        return $this->client->execute(
            $this->source,
            $this->target,
            $this->body,
            $this->contentType,
            $this->params,
            $this->rawQuery,
        );
    }

    private function nested(string $group, string $key, mixed $value): void
    {
        /** @var array<string, mixed> $existing */
        $existing = is_array($this->params[$group] ?? null) ? $this->params[$group] : [];
        $existing[$key] = $value;
        $this->params[$group] = $existing;
    }
}
