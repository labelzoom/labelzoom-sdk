<?php

declare(strict_types=1);

namespace LabelZoom\Sdk\Exception;

/**
 * HTTP 429. The gateway rate-limits on client IP and, when present, on the credential.
 *
 * Retried automatically by the SDK; this surfaces only once the retry budget is exhausted. The
 * exception is {@see PrintQuotaExceededException}, the daily print job limit, which is thrown at
 * once (rule F5).
 */
class RateLimitedException extends LabelZoomException
{
    public function __construct(
        int $status,
        string $message,
        ?string $requestId = null,
        string $rawBody = '',
        private readonly ?int $retryAfterSeconds = null,
        ?string $errorCode = null,
    ) {
        parent::__construct($status, $message, $requestId, $rawBody, $errorCode);
    }

    /** The `Retry-After` header in seconds, when the server sent a parseable one. */
    public function getRetryAfterSeconds(): ?int
    {
        return $this->retryAfterSeconds;
    }
}
