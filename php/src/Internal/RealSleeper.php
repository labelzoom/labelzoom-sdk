<?php

declare(strict_types=1);

namespace LabelZoom\Sdk\Internal;

use LabelZoom\Sdk\Sleeper;

/**
 * Sleeps for real. The default {@see Sleeper}.
 *
 * @internal
 */
final class RealSleeper implements Sleeper
{
    public function sleep(float $seconds): void
    {
        if ($seconds <= 0) {
            return;
        }
        usleep((int) round($seconds * 1_000_000));
    }
}
