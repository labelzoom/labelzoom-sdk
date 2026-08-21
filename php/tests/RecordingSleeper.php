<?php

declare(strict_types=1);

namespace LabelZoom\Sdk\Tests;

use LabelZoom\Sdk\Sleeper;

/**
 * Records what the retry policy asked to sleep, and returns immediately.
 *
 * The retry fixtures assert exact durations. A suite that really slept would add ten seconds of
 * CI time per language and assert nothing extra — which is why rule F4 makes the sleeper a seam.
 */
final class RecordingSleeper implements Sleeper
{
    /** @var list<float> */
    public array $sleeps = [];

    public function sleep(float $seconds): void
    {
        $this->sleeps[] = $seconds;
    }
}
