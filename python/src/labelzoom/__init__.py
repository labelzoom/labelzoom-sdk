"""Official Python SDK for the `LabelZoom <https://www.labelzoom.com>`_ API.

Converts barcode labels between ZPL, EPL, IPL, TSPL, DPL, SBPL, PDF, LabelZoom XML/JSON and
raster images, and prints them on Cloud Print printers.

```python
from labelzoom import LabelZoomClient

with LabelZoomClient() as client:               # anonymous free tier; no key required
    result = client.convert("zpl", "png", "^XA^FO20,20^A0N,28^FDhi^FS^XZ", target_dpi=300)
    print(result.status, result.content_type, len(result.content))
```
"""

from ._version import __version__
from .client import (
    API_KEY_ENV_VAR,
    DEFAULT_BASE_URL,
    AsyncLabelZoomClient,
    AsyncPrintJobs,
    ConversionResult,
    LabelZoomClient,
    PrintJobs,
)
from .errors import (
    BadRequestError,
    ForbiddenError,
    LabelZoomError,
    LabelZoomValidationError,
    NotFoundError,
    PayloadTooLargeError,
    PrintQuotaExceededError,
    RateLimitedError,
    ServerError,
    UnauthorizedError,
    UnprocessableError,
)
from .formats import (
    PRINT_SOURCES,
    SOURCE_FORMATS,
    TARGET_FORMATS,
    ColorMode,
    LayoutFill,
    PdfConversionMode,
    PrintSource,
    SourceFormat,
    TargetFormat,
    ZplImageCompression,
)
from .options import UNSET, ConversionOptions, DataRecord
from .printing import (
    PrintBatch,
    PrintJob,
    PrintJobDetails,
    PrintLimits,
    PrintQuota,
    PrintQuotaCounter,
)

__all__ = [
    "API_KEY_ENV_VAR",
    "DEFAULT_BASE_URL",
    "PRINT_SOURCES",
    "SOURCE_FORMATS",
    "TARGET_FORMATS",
    "UNSET",
    "AsyncLabelZoomClient",
    "AsyncPrintJobs",
    "BadRequestError",
    "ColorMode",
    "ConversionOptions",
    "ConversionResult",
    "DataRecord",
    "ForbiddenError",
    "LabelZoomClient",
    "LabelZoomError",
    "LabelZoomValidationError",
    "LayoutFill",
    "NotFoundError",
    "PayloadTooLargeError",
    "PdfConversionMode",
    "PrintBatch",
    "PrintJob",
    "PrintJobDetails",
    "PrintJobs",
    "PrintLimits",
    "PrintQuota",
    "PrintQuotaCounter",
    "PrintQuotaExceededError",
    "PrintSource",
    "RateLimitedError",
    "ServerError",
    "SourceFormat",
    "TargetFormat",
    "UnauthorizedError",
    "UnprocessableError",
    "ZplImageCompression",
    "__version__",
]
