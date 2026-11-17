<?php

declare(strict_types=1);

namespace LabelZoom\Sdk\Exception;

/**
 * HTTP 422. The request was well formed but can't be carried out.
 *
 * A print returns it with an error code: `agent_update_required` when the print agent serving the
 * printer is too old for the request, `idempotency_key_reused` when the key was already used for a
 * different request, or a template print whose records are missing merge fields. Read
 * {@see self::getErrorCode()} to tell them apart. Not retried.
 */
final class UnprocessableException extends LabelZoomException
{
}
