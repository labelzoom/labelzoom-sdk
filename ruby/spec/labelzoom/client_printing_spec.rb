# frozen_string_literal: true

require "cgi"
require "json"

# The print surface beyond what the shared fixtures pin. The conformance suite drives print
# through the same keyword arguments, so these cover what a fixture cannot: one request
# carrying everything at once, a key per call, every source reaching a printer, and the line
# between the options print takes and the target options it rejects, which Ruby's keyword form
# has to draw at run time.
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

  def query(request) = CGI.parse(request.uri.query)

  # Ruby has no chained stages, so this is its form of the stage-shape test the chaining SDKs
  # carry: print mirrors convert's arguments with the printer in the target's place, and the
  # printer target's own options are exactly copies and the idempotency key, beside the source
  # options and rotation in the splat.
  describe "the call shape" do
    it "mirrors convert with the printer in the target's place" do
      expect(described_class.instance_method(:convert).parameters)
        .to eq([%i[req source], %i[req target], %i[req body], %i[key base64_text],
                %i[keyrest options]])
      expect(described_class.instance_method(:print).parameters)
        .to eq([%i[req source], %i[req printer_id], %i[req body], %i[key copies],
                %i[key idempotency_key], %i[keyrest options]])
    end

    it "takes a template as its source, with data and validate, and a printer as its target" do
      expect(described_class.instance_method(:print_template).parameters)
        .to eq([%i[req template_id], %i[req printer_id], %i[key data], %i[key validate],
                %i[key copies], %i[key idempotency_key]])
    end
  end

  it "sends a document print with its source options, rotation, copies and key" do
    job = client.print(:pdf, "printer 1", "%PDF-1.7",
                       copies: 2, idempotency_key: "SO-1001", pdf: { page_number: 0 },
                       label: { width: 4, height: 6 }, split: { rows: 2, columns: 1 },
                       rotation: 90)

    expect(requests.length).to eq(1)
    request = requests.first
    expect(request.method).to eq(:post)
    # Each id is percent-encoded as one path segment.
    expect(request.uri.path).to eq("/api/v3/printers/printer%201/print")

    expect(query(request)["sourceFormat"]).to eq(["pdf"])
    expect(query(request)["copies"]).to eq(["2"])
    expect(JSON.parse(query(request)["params"].first)).to eq(
      "pdf" => { "pageNumber" => 0 },
      "label" => { "width" => 4, "height" => 6 },
      "split" => { "rows" => 2, "columns" => 1 },
      "rotation" => 90
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
    2.times { client.print(:zpl, "p1", "^XA^XZ") }

    keys = requests.map { |request| header(request, "idempotency-key") }
    expect(keys).to all(match(/\A\h{8}-\h{4}-4\h{3}-[89ab]\h{3}-\h{12}\z/))
    expect(keys.uniq.length).to eq(2)
  end

  describe "the sources" do
    it "prints every source format except a URL" do
      expect(LabelZoom::PRINT_SOURCE_FORMATS)
        .to match_array(LabelZoom::SOURCE_FORMATS - [:url])

      LabelZoom::PRINT_SOURCE_FORMATS.each do |source|
        client.print(source, "p1", "BODY")
        request = requests.last
        expect(query(request)["sourceFormat"]).to eq([LabelZoom::Formats.source_token(source)])
        expect(header(request, "content-type")).to eq(LabelZoom::Formats.media_type(source))
      end
      expect(requests.length).to eq(LabelZoom::PRINT_SOURCE_FORMATS.length)
    end

    it "prints a design file as its own bytes" do
      btw = "\x00\x01BTW\xFF".b
      client.print(:btw, "p1", btw)

      request = requests.first
      expect(query(request)["sourceFormat"]).to eq(["btw"])
      expect(query(request)).not_to have_key("params")
      expect(header(request, "content-type")).to eq("application/octet-stream")
      expect(request.body.b).to eq(btw)
    end

    it "refuses a URL without sending anything" do
      expect { client.print(:url, "p1", "https://example.com/label.pdf") }
        .to raise_error(LabelZoom::ValidationError) { |error| expect(error.parameter).to eq("source") }
      expect(requests).to be_empty
    end

    it "refuses base64 text without sending anything" do
      expect { client.print(:png, "p1", "iVBORw0KGgo=", base64_text: true) }
        .to raise_error(LabelZoom::ValidationError) { |error| expect(error.parameter).to eq("source") }
      expect(requests).to be_empty
    end

    it "takes base64_text: false as the default it is" do
      client.print(:zpl, "p1", "^XA^XZ", base64_text: false)

      expect(header(requests.first, "content-type")).to eq("text/plain")
      expect(query(requests.first)).not_to have_key("params")
    end
  end

  describe "the options" do
    # Every convert option is either one print takes (a source option, or rotation) or a target
    # option it rejects. An option added to convert without deciding which fails here.
    let(:rejected_target_options) do
      { target_dpi: 300, watermark: false, dialect: "zebra", layout: { across: 2 } }
    end

    it "classifies every convert option as one or the other" do
      every_option = LabelZoom::Options::SCALAR_KEYS.keys +
                     LabelZoom::Options::NESTED_KEYS.keys + %i[data extra]

      expect(LabelZoom::Options::PRINT_KEYS)
        .to match_array(LabelZoom::Options::SOURCE_KEYS + %i[rotation])
      expect(LabelZoom::Options::PRINT_KEYS + rejected_target_options.keys)
        .to match_array(every_option)
    end

    it "sends every option print takes" do
      client.print(:png, "p1", "PNG", source_dpi: 300, label: { width: 4, height: 6 },
                                      label_index: 0, data: [{ sku: "A" }],
                                      pdf: { page_number: 0 }, split: { rows: 2 },
                                      scaling: 50, position: { x: 1, y: 2 }, darkness: 60,
                                      color_mode: "BW", zpl: { commands_to_ignore: ["^PQ"] },
                                      rotation: 270, extra: { future: true })

      params = JSON.parse(query(requests.first)["params"].first)
      expect(params.keys).to contain_exactly(
        "sourceDpi", "label", "labelIndex", "data", "pdf", "split", "scaling", "position",
        "darkness", "colorMode", "zpl", "rotation", "future"
      )
    end

    it "validates rotation exactly as convert does" do
      expect { client.print(:zpl, "p1", "^XA^XZ", rotation: 45) }
        .to raise_error(LabelZoom::ValidationError) do |error|
          expect(error.parameter).to eq("rotation")
        end
      expect(requests).to be_empty
    end

    %i[target_dpi watermark dialect layout].each do |key|
      it "rejects the target option #{key} without sending anything" do
        expect { client.print(:zpl, "p1", "^XA^XZ", key => rejected_target_options.fetch(key)) }
          .to raise_error(LabelZoom::ValidationError)
        expect(requests).to be_empty
      end
    end

    it "rejects the target half of zpl" do
      expect { client.print(:zpl, "p1", "^XA^XZ", zpl: { image_compression: "Z64" }) }
        .to raise_error(LabelZoom::ValidationError) { |error| expect(error.parameter).to eq("zpl") }
    end
  end

  it "makes the daily limit a kind of rate limit" do
    expect(LabelZoom::PrintQuotaExceededError.ancestors).to include(LabelZoom::RateLimitedError)
    expect(LabelZoom::UnprocessableError.ancestors).to include(LabelZoom::APIError)
  end
end
