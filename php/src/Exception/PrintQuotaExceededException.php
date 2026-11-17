<?php

declare(strict_types=1);

namespace LabelZoom\Sdk\Exception;

/**
 * HTTP 429 with error code `daily_job_limit_reached`: the account's daily print job limit is used
 * up. It resets at 00:00 UTC, which {@see self::getRetryAfterSeconds()} counts down to.
 *
 * A {@see RateLimitedException}, so a handler for that catches this too. Unlike other rate limits,
 * it is **never** retried automatically, whatever `Retry-After` says: the wait is hours, and
 * honoring it would hang the call (rule F5). Call `LabelZoomClient::getPrintQuota()` to check the
 * remaining count before printing.
 */
final class PrintQuotaExceededException extends RateLimitedException
{
    /** The error code that marks a 429 as the daily print job limit. */
    public const DAILY_JOB_LIMIT_CODE = 'daily_job_limit_reached';
}
