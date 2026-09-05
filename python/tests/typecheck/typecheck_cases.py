"""The two ``typecheck/*`` conformance cases, checked by mypy instead of by pytest.

``conformance/skips/python.json`` declares these two cases skipped because Python has no
compile step. This file is what makes that reason true rather than an excuse: each assertion
below is a ``# type: ignore[...]`` on a call that *must* be a type error, and ``mypy`` is
configured with ``warn_unused_ignores = true``. If any of these calls ever became valid, the
ignore would be unnecessary and mypy would fail the build.

That inverted form is the only way to assert "this must not type-check" without a plugin. Run
it with ``mypy`` -- pytest deliberately does not collect this module.
"""

from __future__ import annotations

from labelzoom import LabelZoomClient

client = LabelZoomClient(None)
body = "^XA^FO20,20^A0N,28^FDhi^FS^XZ"

# typecheck/url-is-not-a-target
#
# 'url' is a fetch instruction, not a format: valid as a source, never as a target.
client.convert("zpl", "url", body)  # type: ignore[arg-type]

# typecheck/source-format-not-accepted-as-target
#
# 'pdf' is in both unions, so it cannot be the discriminator. What this case really pins is
# that SourceFormat is strictly wider than TargetFormat: a value typed only as a SourceFormat
# is not assignable to the target parameter, even when the specific member overlaps.
source: str = "pdf"
client.convert("zpl", source, body)  # type: ignore[arg-type]

# The positive controls. If a refactor made `convert` accept `Any` -- the failure mode that
# would silently defeat every assertion above -- these would still pass while the ignores
# above became unused, and mypy would say so.
client.convert("zpl", "png", body)
client.convert("jpg", "jpeg", body, dpi=300, label_width=4.0, pdf_page_number=0)

# The printer languages are targets as of the 1.1.0 contract, not just sources -- and ipl and
# sbpl joined them in 1.2.0. These are positive controls too: were the Literal ever narrowed
# back, mypy would flag them here.
client.convert("zpl", "epl", body)
client.convert("pdf", "tspl", body)
client.convert("epl", "dpl", body)
client.convert("zpl", "ipl", body)
client.convert("ipl", "sbpl", body)
client.convert("sbpl", "zpl", body)
