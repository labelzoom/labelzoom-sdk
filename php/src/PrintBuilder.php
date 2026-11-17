<?php

declare(strict_types=1);

namespace LabelZoom\Sdk;

/**
 * Prints LabelZoom XML or JSON. They record their own resolution, so there is no `withDpi()`.
 *
 * The print twin of {@see SourceBuilder}.
 */
final class PrintBuilder extends PrintStage
{
}
