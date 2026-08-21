<?php

declare(strict_types=1);

namespace LabelZoom\Sdk\Exception;

/** HTTP 401. The credential was missing or malformed. Send `Authorization: Bearer <key>`. */
final class UnauthorizedException extends LabelZoomException
{
}
