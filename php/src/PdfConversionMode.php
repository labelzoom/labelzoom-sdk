<?php

declare(strict_types=1);

namespace LabelZoom\Sdk;

/** How a source PDF is interpreted. The server default is {@see self::Image}. */
enum PdfConversionMode: string
{
    /** Rasterize the page, then convert the image. The server default. */
    case Image = 'IMAGE';

    /** Read the PDF's own drawing operators. */
    case Native = 'NATIVE';
}
