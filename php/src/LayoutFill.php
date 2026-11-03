<?php

declare(strict_types=1);

namespace LabelZoom\Sdk;

/**
 * How pages fill the columns of a multi-column row, when
 * {@see ConversionTargetBuilder::withLayout()} puts more than one label across. The server
 * default is {@see self::Sequential}.
 */
enum LayoutFill: string
{
    /** Each page goes in the next column. The server default. */
    case Sequential = 'SEQUENTIAL';

    /** Each page fills the whole row. */
    case Repeat = 'REPEAT';
}
