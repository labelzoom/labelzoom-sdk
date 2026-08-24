# frozen_string_literal: true

module LabelZoom
  # The outcome of a successful conversion.
  #
  # {#bytes} is authoritative. PDF, PNG, BMP, GIF and JPEG targets are binary, so treating
  # the response as text would silently corrupt five of the eleven targets -- and EPL and
  # TSPL reach the same hazard through a +text/plain+ response, because their +GW+ and
  # +BITMAP+ commands inline a raw 1-bpp payload.
  class ConversionResult
    # @return [String] the response body as binary (ASCII-8BIT), exactly as the server
    #   sent it. Note this is a String of bytes, not String#bytes' Array of Integers.
    attr_reader :bytes

    # @return [String, nil] the response Content-Type header.
    attr_reader :content_type

    # @return [Integer] the HTTP status code, always 2xx here.
    attr_reader :status

    # @return [String, nil] the X-LZ-Request-Id response header. The support handle.
    attr_reader :request_id

    def initialize(bytes:, status:, content_type: nil, request_id: nil)
      @bytes = bytes
      @status = status
      @content_type = content_type
      @request_id = request_id
    end

    # {#bytes} decoded with the response charset, defaulting to UTF-8.
    #
    # Safe for the textual targets. For a binary target, or an EPL/TSPL label that might
    # carry graphics, read {#bytes} instead.
    #
    # Memoized rather than computed in the constructor: a 5 MB PNG's bytes should never be
    # run through a decoder just because someone built a result object.
    #
    # @return [String]
    def text
      @text ||= decode
    end

    # Writes {#bytes} to +path+, in binary mode.
    def save(path)
      File.binwrite(path, bytes)
      path
    end

    def to_s = text

    private

    # Not a hardcoded UTF-8 decode: the API serves ISO-8859-1 for some conversions, and a
    # byte like 0xE9 is not valid UTF-8 -- it would come back as U+FFFD rather than "é".
    def decode
      charset = content_type.to_s[/charset\s*=\s*"?([^;"]+)"?/i, 1]&.strip
      source = charset.nil? || charset.empty? ? Encoding::UTF_8 : Encoding.find(charset)
      bytes.dup.force_encoding(source).encode(Encoding::UTF_8, invalid: :replace, undef: :replace)
    rescue ArgumentError, Encoding::ConverterNotFoundError
      # An unrecognized charset is not worth failing an otherwise good conversion.
      bytes.dup.force_encoding(Encoding::UTF_8).scrub
    end
  end
end
