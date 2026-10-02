<?php

declare(strict_types=1);

namespace LabelZoom\Sdk;

/** How color is handled when rasterizing. The server default is {@see self::Grayscale}. */
enum ColorMode: string
{
    /** Pure black and white, thresholded by `darkness`. */
    case Bw = 'BW';

    /** Grayscale. The server default. */
    case Grayscale = 'GRAYSCALE';

    /** Full color. */
    case Color = 'COLOR';
}
