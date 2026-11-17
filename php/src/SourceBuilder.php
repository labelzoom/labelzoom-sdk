<?php

declare(strict_types=1);

namespace LabelZoom\Sdk;

/**
 * The source stage for LabelZoom XML and JSON. They record their own resolution, so there is no
 * `withDpi()`.
 */
final class SourceBuilder extends SourceStage
{
}
