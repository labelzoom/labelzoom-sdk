<?php

declare(strict_types=1);

namespace LabelZoom\Sdk;

/** Image compression used when writing ZPL. The server default is {@see self::Z64}. */
enum ZplImageCompression: string
{
    /** Base64-encoded LZ77. The server default. */
    case Z64 = 'Z64';

    /** ASCII hex with run-length compression. */
    case CompressedHex = 'COMPRESSED_HEX';
}
