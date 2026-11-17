<?php

declare(strict_types=1);

namespace LabelZoom\Sdk\Internal;

/**
 * Reads members out of a decoded JSON response, tolerantly.
 *
 * A member that is missing or has an unexpected type reads as null rather than failing the call,
 * and members the SDK doesn't know are ignored, so the server can add them (rule P6).
 *
 * @internal
 */
final class JsonReader
{
    /**
     * @param array<mixed> $json
     */
    public static function string(array $json, string $name): ?string
    {
        $value = $json[$name] ?? null;

        return is_string($value) ? $value : null;
    }

    /**
     * @param array<mixed> $json
     */
    public static function int(array $json, string $name): ?int
    {
        $value = $json[$name] ?? null;

        return is_int($value) ? $value : null;
    }

    /**
     * @param array<mixed> $json
     */
    public static function isTrue(array $json, string $name): bool
    {
        return ($json[$name] ?? null) === true;
    }

    /**
     * A nested object, or an empty array when the member is missing.
     *
     * @param array<mixed> $json
     *
     * @return array<mixed>
     */
    public static function object(array $json, string $name): array
    {
        $value = $json[$name] ?? null;

        return is_array($value) ? $value : [];
    }

    /**
     * An ISO 8601 timestamp, or null when the member is missing, null or not a timestamp.
     *
     * @param array<mixed> $json
     */
    public static function instant(array $json, string $name): ?\DateTimeImmutable
    {
        $text = self::string($json, $name);
        // An empty string would otherwise parse as "now".
        if ($text === null || trim($text) === '') {
            return null;
        }

        try {
            return new \DateTimeImmutable($text);
        } catch (\Exception) {
            return null;
        }
    }
}
