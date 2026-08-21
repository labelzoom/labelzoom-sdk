<?php

declare(strict_types=1);

namespace LabelZoom\Sdk\Exception;

/** HTTP 404. No such endpoint. Usually an unsupported source/target pair or a wrong base URL. */
final class NotFoundException extends LabelZoomException
{
}
