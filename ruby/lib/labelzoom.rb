# frozen_string_literal: true

require_relative "labelzoom/version"
require_relative "labelzoom/errors"
require_relative "labelzoom/formats"
require_relative "labelzoom/options"
require_relative "labelzoom/conversion_result"
require_relative "labelzoom/client"

# Official Ruby client for the LabelZoom API.
#
# LabelZoom converts barcode labels between printer languages (ZPL, EPL, IPL, TSPL, DPL, SBPL),
# LabelZoom's own XML/JSON model, PDF, and raster images. Almost everything the API does
# happens at one endpoint:
#
#     POST https://api.labelzoom.com/api/v2/convert/{sourceFormat}/to/{targetFormat}
#
#     client = LabelZoom::Client.new
#     result = client.convert(:zpl, :png, "^XA^FO20,20^A0N,28^FDhello^FS^XZ",
#                             label: { width: 4, height: 6 })
#     result.save("label.png")
#
# The behaviour of every LabelZoom SDK is specified in docs/API_CONTRACT.md and checked by
# the shared fixtures in conformance/, which this gem's spec suite executes.
module LabelZoom
end
