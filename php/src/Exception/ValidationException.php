<?php

declare(strict_types=1);

namespace LabelZoom\Sdk\Exception;

/**
 * A call the SDK rejected locally, before any HTTP request was made.
 *
 * Deliberately **not** a {@see LabelZoomException}: it carries no status, no request id and no
 * response body because there was no response, and it must never be retried (rule C6). A caller
 * catching `LabelZoomException` is asking about the API; this is about their own arguments.
 */
final class ValidationException extends \InvalidArgumentException
{
    /**
     * @param string $parameter the offending parameter — `rotation`, `data`, `body`, ...
     */
    public function __construct(private readonly string $parameter, string $message)
    {
        parent::__construct($message);
    }

    /** Which parameter was rejected. */
    public function getParameter(): string
    {
        return $this->parameter;
    }
}
