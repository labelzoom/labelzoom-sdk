# frozen_string_literal: true

# conformance/skips/ruby.json declares both typecheck/* cases skipped, with the reason
# "Ruby has no compile step. Target validation is enforced at runtime by raising
# ArgumentError; covered by the validation/* cases instead."
#
# These examples are what make that reason TRUE rather than merely convenient. A skip whose
# stated justification is never checked is how a conformance suite quietly becomes
# decorative -- the same role python/tests/typecheck/typecheck_cases.py plays for Python's
# identical pair of skips.

RSpec.describe LabelZoom::Formats do
  describe "the two source-only formats" do
    # typecheck/url-is-not-a-target: URL is a fetch instruction, not a format, so there is
    # no TargetFormat member to name.
    it "rejects :url as a target" do
      expect { described_class.target!(:url) }
        .to raise_error(LabelZoom::ValidationError) { |error| expect(error.parameter).to eq("target") }
    end

    # :jpg is an input spelling that normalizes to jpeg on the wire (rule A2); it is not a
    # target the API produces.
    it "rejects :jpg as a target" do
      expect { described_class.target!(:jpg) }.to raise_error(LabelZoom::ValidationError)
    end

    it "accepts both as sources" do
      expect(described_class.source!(:url)).to eq(:url)
      expect(described_class.source!(:jpg)).to eq(:jpg)
    end

    it "excludes both from TARGET_FORMATS" do
      expect(LabelZoom::TARGET_FORMATS).not_to include(:url, :jpg)
    end
  end

  # typecheck/source-format-not-accepted-as-target: the statically typed SDKs make this a
  # compile error. Ruby's equivalent is that the target set is strictly narrower than the
  # source set, checked on every call.
  it "does not accept every source as a target" do
    expect(LabelZoom::SOURCE_FORMATS - LabelZoom::TARGET_FORMATS).to contain_exactly(:jpg, :url)
  end

  # Positive controls. The printer languages became TARGETS in contract 1.1.0, when the
  # printer-language writers shipped; four typecheck cases asserting the opposite were
  # removed. A rejection test with no positive control would pass just as happily if
  # target! rejected everything.
  it "accepts the printer languages as targets" do
    %i[epl ipl tspl dpl sbpl].each { |format| expect(described_class.target!(format)).to eq(format) }
  end

  it "raises a ValidationError that is an ArgumentError but not an APIError" do
    error = begin
      described_class.target!(:url)
    rescue LabelZoom::ValidationError => e
      e
    end

    # The skip's reason says "raising ArgumentError"; this is that claim, asserted.
    expect(error).to be_a(ArgumentError)
    # A local rejection is a bug in the calling code, not a server response, so code
    # rescuing APIError to implement a fallback must not swallow it.
    expect(error).not_to be_a(LabelZoom::APIError)
  end

  describe "the media-type table" do
    it "covers every source format" do
      expect(described_class::MEDIA_TYPES.keys).to match_array(LabelZoom::SOURCE_FORMATS)
    end

    it "normalizes jpg to jpeg on the wire but keeps its own media type entry" do
      expect(described_class.source_token(:jpg)).to eq("jpeg")
      expect(described_class.media_type(:jpg)).to eq("image/jpeg")
    end
  end
end
