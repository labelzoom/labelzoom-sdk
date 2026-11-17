<?php

declare(strict_types=1);

namespace LabelZoom\Sdk\Internal;

use LabelZoom\Sdk\LabelZoomClient;
use LabelZoom\Sdk\SourceFormat;

/**
 * The request under construction, handed from the source stage to the target stage.
 *
 * Both stages write into the same parameters, because the wire has one `?params=` object, not one
 * per stage. It is an object so both stages share it: a PHP array would be copied when handed on.
 *
 * @internal
 */
final class ConversionDraft
{
    /** @var array<string, mixed> */
    public array $params = [];

    /** @var array<string, string> */
    public array $rawQuery = [];

    public function __construct(
        public readonly LabelZoomClient $client,
        public readonly SourceFormat $source,
        public readonly string $body,
        public readonly string $contentType,
    ) {
    }

    /** Sets one key of a group such as `split`, merging into the group rather than replacing it. */
    public function nested(string $group, string $key, mixed $value): void
    {
        /** @var array<string, mixed> $existing */
        $existing = is_array($this->params[$group] ?? null) ? $this->params[$group] : [];
        $existing[$key] = $value;
        $this->params[$group] = $existing;
    }
}
