<?php

declare(strict_types=1);

namespace LabelZoom\Sdk\Http;

use Psr\Http\Client\ClientExceptionInterface;

/**
 * A cURL-level failure from {@see CurlHttpClient}.
 *
 * Implements the PSR-18 marker interface so the retry policy recognizes it as a transport
 * failure regardless of which PSR-18 client produced it.
 */
final class CurlTransportException extends \RuntimeException implements ClientExceptionInterface
{
}
