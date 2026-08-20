<?php

declare(strict_types=1);

namespace LabelZoom\Sdk;

/**
 * The delay between retry attempts.
 *
 * A seam rather than a bare `usleep`, because rule F4 of the API contract makes it one: without
 * it the retry tests either burn wall-clock seconds or go untested. {@see Internal\RealSleeper}
 * is the default; substitute a recorder to assert your own retry handling without waiting for it.
 *
 * Public, and part of the supported surface — it appears in {@see LabelZoomClient::__construct()}.
 */
interface Sleeper
{
    /** @param float $seconds how long the retry policy wants to wait; may be fractional */
    public function sleep(float $seconds): void;
}
