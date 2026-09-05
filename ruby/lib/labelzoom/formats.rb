# frozen_string_literal: true

module LabelZoom
  # The format metadata table, defined once.
  #
  # Deliberately the only place a format's media type appears. The superseded .NET design
  # had a builder class per format, each independently knowing this mapping, and they
  # drifted -- one of them emitted the source type as the target.
  module Formats
    # Every accepted source, in the contract's order. +:jpg+ is an input spelling that
    # normalizes to +:jpeg+ on the wire; +:url+ tells the server to go fetch a document
    # rather than naming a format.
    SOURCE_FORMATS = %i[zpl epl ipl tspl dpl sbpl xml json pdf png bmp gif jpeg jpg url].freeze

    # Every accepted target. There is no +:url+ -- it is a fetch instruction, not an output
    # format -- and no +:jpg+, which is a source-side spelling only.
    #
    # The printer languages round-trip: +:epl+, +:tspl+ and +:dpl+ became targets in
    # contract 1.1.0, when the printer-language writers shipped, and +:ipl+ and +:sbpl+
    # joined them in 1.2.0 with the Intermec and SATO adapters.
    TARGET_FORMATS = %i[zpl epl ipl tspl dpl sbpl xml json pdf png bmp gif jpeg].freeze

    MEDIA_TYPES = {
      zpl: "text/plain",
      epl: "text/plain",
      ipl: "text/plain",
      tspl: "text/plain",
      dpl: "text/plain",
      sbpl: "text/plain",
      xml: "application/xml",
      json: "application/json",
      pdf: "application/pdf",
      png: "image/png",
      bmp: "image/bmp",
      gif: "image/gif",
      jpeg: "image/jpeg",
      jpg: "image/jpeg",
      # The body is the URL itself.
      url: "text/plain"
    }.freeze

    # Colour reduction. Server default GRAYSCALE.
    COLOR_MODES = %w[BW GRAYSCALE COLOR].freeze

    # How a PDF source is interpreted. Server default IMAGE.
    PDF_CONVERSION_MODES = %w[IMAGE NATIVE].freeze

    # Encoding of images embedded in ZPL output. Server default Z64.
    ZPL_IMAGE_COMPRESSIONS = %w[Z64 COMPRESSED_HEX].freeze

    module_function

    # @return [Symbol] the validated source format.
    # @raise [ValidationError] if it is not a source the API accepts.
    def source!(format)
      symbol = format.to_s.downcase.to_sym
      return symbol if SOURCE_FORMATS.include?(symbol)

      raise ValidationError.new("source",
                                "#{format.inspect} is not a source format the LabelZoom API " \
                                "accepts. Expected one of: #{SOURCE_FORMATS.join(", ")}.")
    end

    # @return [Symbol] the validated target format.
    # @raise [ValidationError] if it is not a target the API produces. This is where Ruby
    #   enforces what the statically typed SDKs enforce at compile time -- passing +:url+
    #   or +:jpg+ as a target is caught here.
    def target!(format)
      symbol = format.to_s.downcase.to_sym
      return symbol if TARGET_FORMATS.include?(symbol)

      raise ValidationError.new("target",
                                "#{format.inspect} is not a target format the LabelZoom API " \
                                "produces. Expected one of: #{TARGET_FORMATS.join(", ")}.")
    end

    # The path segment for a source. +:jpg+ normalizes to "jpeg" (rule A2).
    def source_token(format) = format == :jpg ? "jpeg" : format.to_s

    # The path segment for a target. Targets need no normalization.
    def target_token(format) = format.to_s

    # The request Content-Type for a source.
    def media_type(format) = MEDIA_TYPES.fetch(format)
  end

  SOURCE_FORMATS = Formats::SOURCE_FORMATS
  TARGET_FORMATS = Formats::TARGET_FORMATS
end
