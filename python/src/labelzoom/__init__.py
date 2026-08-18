"""Official Python SDK for the `LabelZoom <https://www.labelzoom.com>`_ API.

Converts barcode labels between ZPL, EPL, TSPL, DPL, PDF, LabelZoom XML/JSON and raster
images.

```python
from labelzoom import LabelZoomClient

with LabelZoomClient() as client:               # anonymous free tier; no key required
    result = client.convert("zpl", "png", "^XA^FO20,20^A0N,28^FDhi^FS^XZ", dpi=300)
    print(result.status, result.content_type, len(result.content))
```
"""

from ._version import __version__
from .client import (
    API_KEY_ENV_VAR,
    DEFAULT_BASE_URL,
    AsyncLabelZoomClient,
    ConversionResult,
    LabelZoomClient,
)
from .errors import (
    BadRequestError,
    ForbiddenError,
    LabelZoomError,
    LabelZoomValidationError,
    NotFoundError,
    PayloadTooLargeError,
    RateLimitedError,
    ServerError,
    UnauthorizedError,
)
from .formats import (
    SOURCE_FORMATS,
    TARGET_FORMATS,
    ColorMode,
    PdfConversionMode,
    SourceFormat,
    TargetFormat,
    ZplImageCompression,
)
from .options import UNSET, ConversionOptions, DataRecord

__all__ = [
    "API_KEY_ENV_VAR",
    "DEFAULT_BASE_URL",
    "SOURCE_FORMATS",
    "TARGET_FORMATS",
    "UNSET",
    "AsyncLabelZoomClient",
    "BadRequestError",
    "ColorMode",
    "ConversionOptions",
    "ConversionResult",
    "DataRecord",
    "ForbiddenError",
    "LabelZoomClient",
    "LabelZoomError",
    "LabelZoomValidationError",
    "NotFoundError",
    "PayloadTooLargeError",
    "PdfConversionMode",
    "RateLimitedError",
    "ServerError",
    "SourceFormat",
    "TargetFormat",
    "UnauthorizedError",
    "ZplImageCompression",
    "__version__",
]
