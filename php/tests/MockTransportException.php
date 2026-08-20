<?php

declare(strict_types=1);

namespace LabelZoom\Sdk\Tests;

use Psr\Http\Client\ClientExceptionInterface;

/** A scripted transport failure. PSR-18 marks these with ClientExceptionInterface. */
final class MockTransportException extends \RuntimeException implements ClientExceptionInterface
{
}
