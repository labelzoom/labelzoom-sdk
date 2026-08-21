<?php

declare(strict_types=1);

namespace LabelZoom\Sdk\Exception;

use Psr\Http\Client\ClientExceptionInterface;

/**
 * The request never produced a response — DNS failure, connection refused, TLS error, timeout.
 *
 * Thrown only after the retry budget is exhausted; transport failures are retryable (rule F1).
 * Not a {@see LabelZoomException} for the same reason {@see ValidationException} is not: there is
 * no status, no body and no request id to carry.
 */
final class TransportException extends \RuntimeException
{
    public function __construct(string $message, ClientExceptionInterface $previous)
    {
        parent::__construct($message, 0, $previous);
    }
}
