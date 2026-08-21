<?php

declare(strict_types=1);

namespace LabelZoom\Sdk\Exception;

/** HTTP 5xx. Retried automatically; this surfaces once the retry budget is exhausted. */
final class ServerErrorException extends LabelZoomException
{
}
