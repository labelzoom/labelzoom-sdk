# frozen_string_literal: true

require "json"

# See lib/labelzoom.rb for the module overview.
module LabelZoom
  # The sentinel for "the caller did not pass this".
  #
  # Not +nil+, and not a missing key in a +**options+ splat. +watermark: false+ and
  # +pdf: { page_number: 0 }+ are meaningful values a caller sets deliberately, so
  # falsiness cannot stand in for absence -- the same reason the Python SDK carries an
  # +UNSET+ singleton.
  UNSET = Object.new
  def UNSET.inspect = "LabelZoom::UNSET"
  UNSET.freeze

  # Renders conversion options as the +params+ JSON value.
  #
  # Everything travels in one <tt>?params=&lt;JSON&gt;</tt> parameter, never dot-notation.
  # The API accepts both and merges them, but dot-notation cannot express every parameter
  # -- <tt>?data=[{}]</tt> is rejected with 400 while <tt>?params={"data":[{}]}</tt>
  # succeeds. One serialization path, no special cases.
  module Options
    # Nested option hashes, and the wire key each of their keys maps to. Anything not
    # listed raises rather than being dropped: Ruby has no compiler to catch
    # `label: { widht: 4 }`, and the server ignores unknown keys, so a silent drop would
    # hand the caller a wrong label and no signal.
    NESTED_KEYS = {
      position: { x: "x", y: "y" },
      label: { width: "width", height: "height" },
      pdf: { conversion_mode: "conversionMode", page_number: "pageNumber" },
      zpl: { commands_to_ignore: "commandsToIgnore", image_compression: "imageCompression" }
    }.freeze

    SCALAR_KEYS = {
      dpi: "dpi",
      rotation: "rotation",
      scaling: "scaling",
      color_mode: "colorMode",
      darkness: "darkness",
      watermark: "watermark",
      dialect: "dialect"
    }.freeze

    module_function

    # @return [String, nil] the params JSON, or nil when nothing was set -- in which case
    #   no query parameter is emitted at all (rule C7).
    def serialize(**options)
      params = {}

      SCALAR_KEYS.each do |key, wire_key|
        value = options.fetch(key, UNSET)
        next if UNSET.equal?(value)

        params[wire_key] = validate_scalar(key, value)
      end

      NESTED_KEYS.each do |key, mapping|
        value = options.fetch(key, UNSET)
        next if UNSET.equal?(value)

        params[key.to_s] = nested(key, value, mapping)
      end

      data = options.fetch(:data, UNSET)
      params["data"] = normalize_data(data) unless UNSET.equal?(data)

      extra = options.fetch(:extra, UNSET)
      params.merge!(stringify(extra)) unless UNSET.equal?(extra)

      params.empty? ? nil : JSON.generate(params)
    end

    def validate_scalar(key, value)
      case key
      when :rotation
        # Rejected locally: the server would 400, and this is unambiguously a caller bug.
        unless value.is_a?(Numeric) && (value % 90).zero?
          raise ValidationError.new("rotation",
                                    "Rotation must be a multiple of 90 degrees, but was #{value}.")
        end
      when :darkness
        unless value.is_a?(Numeric) && value.between?(0, 100)
          raise ValidationError.new("darkness",
                                    "Darkness must be between 0 and 100, but was #{value}.")
        end
      end
      value.is_a?(Symbol) ? value.to_s : value
    end

    def nested(key, value, mapping)
      unless value.is_a?(Hash)
        raise ValidationError.new(key.to_s, "#{key} must be a Hash, but was #{describe(value)}.")
      end

      value.each_with_object({}) do |(nested_key, nested_value), out|
        wire_key = mapping[nested_key.to_sym]
        unless wire_key
          raise ValidationError.new(key.to_s,
                                    "#{key}[#{nested_key.inspect}] is not a recognized option. " \
                                    "Expected one of: #{mapping.keys.join(", ")}.")
        end
        next if UNSET.equal?(nested_value)

        out[wire_key] = nested_value.is_a?(Symbol) ? nested_value.to_s : nested_value
      end
    end

    # +data+ is always an array -- one entry produces one label. A caller passing a single
    # record means "one label", so a bare Hash is wrapped rather than rejected.
    def normalize_data(value)
      records = value.is_a?(Array) ? value : [value]

      records.each_with_index.map do |record, index|
        unless record.is_a?(Hash)
          raise ValidationError.new("data",
                                    "data[#{index}] is #{describe(record)}; every entry must be " \
                                    "an object whose keys are the label's variable field names.")
        end
        stringify(record)
      end
    end

    def stringify(hash)
      unless hash.is_a?(Hash)
        raise ValidationError.new("extra", "extra must be a Hash, but was #{describe(hash)}.")
      end

      hash.each_with_object({}) { |(key, value), out| out[key.to_s] = value }
    end

    def describe(value)
      case value
      when nil then "null"
      when Array then "an array"
      when String then "a string"
      when Numeric then "a number"
      when true, false then "a boolean"
      else "a #{value.class}"
      end
    end
  end
end
