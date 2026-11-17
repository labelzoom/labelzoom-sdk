<?php

declare(strict_types=1);

namespace LabelZoom\Sdk;

/**
 * The source stage for LabelZoom XML and JSON, NiceLabel, BarTender and DYMO. These record their own
 * resolution or measure in physical units, so there is no `withDpi()`.
 */
final class SourceBuilder extends SourceStage
{
}
