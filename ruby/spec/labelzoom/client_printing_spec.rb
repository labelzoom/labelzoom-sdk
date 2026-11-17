# frozen_string_literal: true

require "cgi"
require "json"

# The print surface beyond what the shared fixtures pin. The conformance suite drives print
# through the same keyword arguments, so these cover what a fixture cannot: one request
# carrying everything at once, a key per call, and the line between source and target
# options that Ruby's keyword form has to draw at run time.
RSpec.describe LabelZoom::Client, "#print" do
  let(:requests) { [] }
  let(:client) { described_class.new(api_key: "lz_test", max_retries: 0, env: {}) }

  before do
    stub_request(:any, /.*/).to_return do |request|
      requests << request
      { status: 200, headers: { "content-type" => "application/json" },
        body: '{"jobId":"j1","status":"queued","copies":2}' }
    end
  end

  def header(request, name)
    (request.headers || {}).transform_keys { |key| key.to_s.downcase }[name]
  end

  it "sends a document print with its source options, copies and key" do
    job = client.print("printer 1", :pdf, "%PDF-1.7",
                       copies: 2, idempotency_key: "SO-1001", pdf: { page_number: 0 },
                       label: { width: 4, height: 6 }, split: { rows: 2, columns: 1 })

    expect(requests.length).to eq(1)
    request = requests.first
    expect(request.method).to eq(:post)
    # Each id is percent-encoded as one path segment.
    expect(request.uri.path).to eq("/api/v3/printers/printer%201/print")

    query = CGI.parse(request.uri.query)
    expect(query["sourceFormat"]).to eq(["pdf"])
    expect(query["copies"]).to eq(["2"])
    expect(JSON.parse(query["params"].first)).to eq(
      "pdf" => { "pageNumber" => 0 },
      "label" => { "width" => 4, "height" => 6 },
      "split" => { "rows" => 2, "columns" => 1 }
    )

    expect(header(request, "content-type")).to eq("application/pdf")
    expect(header(request, "accept")).to eq("application/json")
    expect(header(request, "idempotency-key")).to eq("SO-1001")
    expect(request.body).to eq("%PDF-1.7")

    expect(job).to eq(LabelZoom::PrintJob.new(job_id: "j1", status: "queued", copies: 2,
                                              idempotent: false))
    expect(job.idempotent?).to be(false)
  end

  it "generates a different key for each call" do
    2.times { client.print("p1", :zpl, "^XA^XZ") }

    keys = requests.map { |request| header(request, "idempotency-key") }
    expect(keys).to all(match(/\A\h{8}-\h{4}-4\h{3}-[89ab]\h{3}-\h{12}\z/))
    expect(keys.uniq.length).to eq(2)
  end

  describe "the source options" do
    # Ruby has no chained stages, so this is its form of the stage-shape test the chaining
    # SDKs carry: every convert option is either a source option print takes or a target
    # option it rejects. An option added to convert without deciding which fails here.
    let(:target_options) do
      { target_dpi: 300, rotation: 90, watermark: false, dialect: "zebra", layout: { across: 2 } }
    end

    it "classifies every convert option as one or the other" do
      every_option = LabelZoom::Options::SCALAR_KEYS.keys +
                     LabelZoom::Options::NESTED_KEYS.keys + %i[data extra]

      expect(LabelZoom::Options::SOURCE_KEYS + target_options.keys).to match_array(every_option)
    end

    it "sends every source option" do
      client.print("p1", :png, "PNG", source_dpi: 300, label: { width: 4, height: 6 },
                                      label_index: 0, data: [{ sku: "A" }],
                                      pdf: { page_number: 0 }, split: { rows: 2 },
                                      scaling: 50, position: { x: 1, y: 2 }, darkness: 60,
                                      color_mode: "BW", zpl: { commands_to_ignore: ["^PQ"] },
                                      extra: { future: true })

      params = JSON.parse(CGI.parse(requests.first.uri.query)["params"].first)
      expect(params.keys).to contain_exactly(
        "sourceDpi", "label", "labelIndex", "data", "pdf", "split", "scaling", "position",
        "darkness", "colorMode", "zpl", "future"
      )
    end

    %i[target_dpi rotation watermark dialect layout].each do |key|
      it "rejects the target option #{key} without sending anything" do
        expect { client.print("p1", :zpl, "^XA^XZ", key => target_options.fetch(key)) }
          .to raise_error(LabelZoom::ValidationError)
        expect(requests).to be_empty
      end
    end

    it "rejects the target half of zpl" do
      expect { client.print("p1", :zpl, "^XA^XZ", zpl: { image_compression: "Z64" }) }
        .to raise_error(LabelZoom::ValidationError) { |error| expect(error.parameter).to eq("zpl") }
    end

    it "rejects base64 text as a source" do
      expect { client.print("p1", :png, "iVBORw0KGgo=", base64_text: true) }
        .to raise_error(LabelZoom::ValidationError) do |error|
          expect(error.parameter).to eq("source")
        end
    end
  end

  it "makes the daily limit a kind of rate limit" do
    expect(LabelZoom::PrintQuotaExceededError.ancestors).to include(LabelZoom::RateLimitedError)
    expect(LabelZoom::UnprocessableError.ancestors).to include(LabelZoom::APIError)
  end
end
