<?php

declare(strict_types=1);

namespace LabelZoom\Sdk\Exception;

/**
 * HTTP 403. The credential is valid but the account may not do this.
 *
 * On the anonymous free tier this is overwhelmingly the paid-feature wall, so that case gets a
 * first-class flag rather than leaving every caller to string-match the message (rule E5).
 */
final class ForbiddenException extends LabelZoomException
{
    public function __construct(
        int $status,
        string $message,
        ?string $requestId = null,
        string $rawBody = '',
        private readonly bool $paidFeature = false,
    ) {
        parent::__construct($status, $message, $requestId, $rawBody);
    }

    /**
     * Whether the server said this needs a paid plan.
     *
     * True for the three common anonymous-tier refusals: `JSON export is a paid feature`,
     * `MOCA export is a paid feature`, and `Conversion between image and document formats is a
     * paid feature`.
     */
    public function isPaidFeature(): bool
    {
        return $this->paidFeature;
    }
}
