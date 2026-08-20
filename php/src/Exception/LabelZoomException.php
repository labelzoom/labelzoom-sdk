<?php

declare(strict_types=1);

namespace LabelZoom\Sdk\Exception;

/**
 * Base type for every error the LabelZoom API returns.
 *
 * One base type so a caller can catch everything with a single handler; subclasses exist so a
 * caller who cares about the difference does not have to switch on a status code (rule E4).
 *
 * Extends `RuntimeException` rather than being a checked-style error, matching the Java SDK's
 * reasoning: an error type that must be declared on every `execute()` is hostile inside a chain.
 */
class LabelZoomException extends \RuntimeException
{
    /**
     * @param int         $status    the HTTP status code
     * @param string      $message   detail extracted from the response body (rule E2)
     * @param string|null $requestId the `X-LZ-Request-Id` header — quote it to LabelZoom support
     * @param string      $rawBody   the response body, never discarded (rule E1)
     */
    public function __construct(
        private readonly int $status,
        string $message,
        private readonly ?string $requestId = null,
        private readonly string $rawBody = '',
    ) {
        parent::__construct($message);
    }

    /** The HTTP status code that produced this error. */
    public function getStatus(): int
    {
        return $this->status;
    }

    /**
     * The `X-LZ-Request-Id` response header, or null if the server did not send one.
     *
     * Surfaced on every error as well as on success: it is the support handle (rule D2).
     */
    public function getRequestId(): ?string
    {
        return $this->requestId;
    }

    /**
     * The response body exactly as received.
     *
     * {@see self::getMessage()} is truncated to 512 characters; this is not.
     */
    public function getRawBody(): string
    {
        return $this->rawBody;
    }
}
