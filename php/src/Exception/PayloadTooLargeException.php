<?php

declare(strict_types=1);

namespace LabelZoom\Sdk\Exception;

/** HTTP 413. The request body exceeded the plan's cap — 1 MB on the anonymous free tier. */
final class PayloadTooLargeException extends LabelZoomException
{
}
