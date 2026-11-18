<?php

declare(strict_types=1);

namespace LabelZoom\Sdk\Internal;

/**
 * The SDK's one JSON encoding, used for `?params=` and for a template print's body.
 *
 * @internal
 */
final class Json
{
    public static function encode(mixed $value): string
    {
        // PRESERVE_ZERO_FRACTION keeps `label.width: 4.0` a float rather than collapsing it to 4;
        // UNESCAPED_SLASHES and UNESCAPED_UNICODE keep the JSON legible in server logs.
        return json_encode(
            $value,
            JSON_THROW_ON_ERROR | JSON_PRESERVE_ZERO_FRACTION | JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE,
        );
    }
}
