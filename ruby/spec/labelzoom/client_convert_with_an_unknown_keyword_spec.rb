# frozen_string_literal: true

# A misspelled keyword must not vanish. The server ignores unknown parameters, so a dropped
# `taget_dpi: 300` would come back as a label at the default resolution, HTTP 200, no signal.
RSpec.describe LabelZoom::Client, "#convert with an unknown keyword" do
  let(:client) { described_class.new(api_key: nil, max_retries: 0) }

  before { stub_request(:any, /api\.labelzoom\.com/).to_return(status: 200, body: "^XA^XZ") }

  def rejection(**options)
    client.convert(:zpl, :pdf, "^XA^XZ", **options)
    nil
  rescue LabelZoom::ValidationError => e
    e
  end

  it "points the 1.x dpi: at the two 2.0 keywords" do
    error = rejection(dpi: 203)
    expect(error).to be_a(LabelZoom::ValidationError)
    expect(error.parameter).to eq("dpi")
    expect(error.message).to include("source_dpi:", "target_dpi:")
    expect(a_request(:any, /api\.labelzoom\.com/)).not_to have_been_made
  end

  it "names a misspelled keyword" do
    error = rejection(taget_dpi: 300)
    expect(error).to be_a(LabelZoom::ValidationError)
    expect(error.parameter).to eq("taget_dpi")
    expect(a_request(:any, /api\.labelzoom\.com/)).not_to have_been_made
  end

  it "still sends every keyword it knows" do
    client.convert(:zpl, :png, "^XA^XZ", source_dpi: 300, target_dpi: 203, rotation: 90,
                                         label: { width: 4, height: 6 }, extra: { "foo" => 1 })
    expect(a_request(:post, /api\.labelzoom\.com/).with do |request|
      params = JSON.parse(URI.decode_www_form(request.uri.query).to_h["params"])
      params == { "sourceDpi" => 300, "targetDpi" => 203, "rotation" => 90,
                  "label" => { "width" => 4, "height" => 6 }, "foo" => 1 }
    end).to have_been_made
  end
end
