# frozen_string_literal: true

# Runs the shared conformance fixtures against the Ruby SDK.
#
# Entirely offline: every request is answered by a WebMock stub, so this passes identically
# on a fork pull request with no secrets. The fixtures are the same ones the .NET, Node,
# Java, Python, PHP and Go suites run -- see docs/CONFORMANCE.md.

# base64 left out deliberately: it stopped being a default gem in Ruby 3.4, and
# Array#pack("m0") / String#unpack1("m") are core, so the suite needs no gem for it.
require "cgi"
require "json"
require "pathname"
require_relative "support/recording_sleeper"

LANGUAGE = "ruby"

def find_conformance_root
  Pathname.new(__dir__).ascend do |directory|
    candidate = directory / "conformance"
    return candidate if (candidate / "cases").directory?
  end
  raise "Could not locate the conformance/ directory."
end

ROOT = find_conformance_root
SPEC = JSON.parse((ROOT / "spec.json").read)
SKIPS = begin
  path = ROOT / "skips" / "#{LANGUAGE}.json"
  path.exist? ? JSON.parse(path.read)["skips"].to_h { |s| [s["id"], s["reason"]] } : {}
end
ALL_CASE_IDS = SPEC["cases"]
EXPECTED_CASE_IDS = ALL_CASE_IDS.reject { |id| SKIPS.key?(id) }
# Mutable on purpose: this is the accumulator the completeness assertion reads.
EXECUTED = [] # rubocop:disable Style/MutableConstant

ERROR_KINDS = {
  "BadRequest" => LabelZoom::BadRequestError,
  "Unauthorized" => LabelZoom::UnauthorizedError,
  "Forbidden" => LabelZoom::ForbiddenError,
  "NotFound" => LabelZoom::NotFoundError,
  "PayloadTooLarge" => LabelZoom::PayloadTooLargeError,
  "RateLimited" => LabelZoom::RateLimitedError,
  "ServerError" => LabelZoom::ServerError
}.freeze

# What a request/* case gets back: those assertions are about what went out.
OK_RESPONSE = { "status" => 200, "headers" => { "content-type" => "text/plain" },
                "bodyText" => "^XA^XZ" }.freeze

# Translates one scripted response into the shape WebMock's to_return wants. A
# transportError entry becomes an exception instead.
def to_webmock(scripted)
  return { exception: Errno::ECONNREFUSED } if scripted.key?("transportError")

  body =
    if scripted.key?("bodyBase64")
      scripted["bodyBase64"].unpack1("m")
    elsif scripted.key?("bodyTextRepeat")
      fill, count = scripted["bodyTextRepeat"]
      fill * count
    else
      scripted.fetch("bodyText", "")
    end

  { status: scripted["status"], headers: scripted["headers"] || {}, body: body }
end

# Translates the fixture's wire-shaped options into the SDK's nested keyword form.
#
# This translation layer is the only per-language code in a runner, and writing it is what
# proves the Ruby divergence in API_CONTRACT.md section 9 is skin deep.
def convert_kwargs(given)
  kwargs = {}
  kwargs[:base64_text] = true if given["sourceEncoding"] == "base64text"

  (given["options"] || {}).each do |key, value|
    case key
    when "sourceDpi" then kwargs[:source_dpi] = value
    when "targetDpi" then kwargs[:target_dpi] = value
    when "colorMode" then kwargs[:color_mode] = value
    when "position" then kwargs[:position] = { x: value["x"], y: value["y"] }
    when "label" then kwargs[:label] = symbolize(value, "width" => :width, "height" => :height)
    when "pdf"
      kwargs[:pdf] = symbolize(value, "conversionMode" => :conversion_mode,
                                      "pageNumber" => :page_number)
    when "zpl"
      kwargs[:zpl] = symbolize(value, "commandsToIgnore" => :commands_to_ignore,
                                      "imageCompression" => :image_compression)
    when "dpi", "rotation", "scaling", "darkness", "watermark", "dialect", "data"
      kwargs[key.to_sym] = value
    else
      raise "Fixture sets option '#{key}', which the Ruby runner does not map. " \
            "Add it to convert_kwargs rather than skipping the case."
    end
  end

  kwargs
end

def symbolize(hash, mapping)
  hash.each_with_object({}) { |(key, value), out| out[mapping.fetch(key)] = value }
end

# Executes one fixture and returns everything the assertions need.
def run_fixture(given, script, default_max_retries: nil)
  sleeper = RecordingSleeper.new
  requests = []
  WebMock.after_request { |signature, _| requests << signature }

  stub_request(:any, /.*/).to_return(*script.map { |entry| to_webmock(entry) })

  client = LabelZoom::Client.new(**client_kwargs(given, default_max_retries),
                                 sleeper: sleeper, jitter: false)

  result = nil
  error = nil
  begin
    result = client.convert(given["source"].to_sym, given["target"].to_sym,
                            given["bodyText"], **convert_kwargs(given))
  rescue LabelZoom::APIError, LabelZoom::ValidationError, LabelZoom::TransportError => e
    error = e
  end

  { result: result, error: error, requests: requests, sleeper: sleeper }
ensure
  WebMock.reset_callbacks
end

def client_kwargs(given, default_max_retries)
  spec = given["client"]
  kwargs = {}

  if spec.nil?
    # A case with no client block is not exercising credential resolution. Force anonymous
    # so a developer's real LABELZOOM_API_KEY cannot change the outcome.
    kwargs[:api_key] = nil
  elsif spec.key?("apiKey")
    # Present-and-null means "no credential"; omitting the key entirely means "read the
    # environment", which is the case that must leave api_key at UNSET.
    kwargs[:api_key] = spec["apiKey"]
  end
  kwargs[:base_url] = spec["baseUrl"] if spec&.key?("baseUrl")

  max_retries = given.fetch("maxRetries", default_max_retries)
  kwargs[:max_retries] = max_retries unless max_retries.nil?
  kwargs[:env] = given["env"] || {}
  kwargs
end

# ------------------------------------------------------------------- assertions

def assert_error(expected, actual)
  expect(actual).to be_a(LabelZoom::APIError),
                    -> { "expected an APIError, got #{actual.inspect}" }

  expect(actual).to be_a(ERROR_KINDS.fetch(expected["kind"])) if expected.key?("kind")
  expect(actual.status).to eq(expected["status"]) if expected.key?("status")
  expect(actual.message).to eq(expected["message"]) if expected.key?("message")
  expect(actual.message.strip).not_to be_empty if expected["messageNonEmpty"]
  if expected.key?("messageMaxLength")
    expect(actual.message.length).to be <= expected["messageMaxLength"]
  end
  if expected.key?("rawBodyLength")
    expect(actual.raw_body.bytesize).to eq(expected["rawBodyLength"])
  end
  expect(actual.raw_body).not_to be_empty if expected["rawBodyPresent"]
  expect(actual.request_id).to eq(expected["requestId"]) if expected.key?("requestId")
  expect(actual.paid_feature?).to eq(expected["isPaidFeature"]) if expected.key?("isPaidFeature")
  return unless expected.key?("retryAfterSeconds")

  expect(actual.retry_after_seconds).to eq(expected["retryAfterSeconds"])
end

def run_request_case(given, expect_block)
  outcome = run_fixture(given, [OK_RESPONSE])
  expect(outcome[:error]).to be_nil, -> { "unexpected error: #{outcome[:error].inspect}" }

  signature = outcome[:requests].last
  expect(signature).not_to be_nil
  uri = signature.uri

  expect(signature.method.to_s.upcase).to eq(expect_block["method"]) if expect_block.key?("method")
  if expect_block.key?("url")
    expect("#{uri.scheme}://#{uri.host}#{uri.path}").to eq(expect_block["url"])
  end
  expect(uri.path).to eq(expect_block["path"]) if expect_block.key?("path")

  # WebMock normalizes header names to Capitalized-Dash form, so compare case-insensitively.
  # expect.headers is a subset assertion; headersAbsent is exact.
  headers = (signature.headers || {}).transform_keys { |name| name.to_s.downcase }
  (expect_block["headers"] || {}).each do |name, value|
    expect(headers[name.downcase]).to eq(value)
  end
  (expect_block["headersAbsent"] || []).each do |name|
    expect(headers).not_to have_key(name.downcase)
  end
  (expect_block["headersMatch"] || {}).each do |name, pattern|
    expect(headers[name.downcase].to_s).to match(Regexp.new(pattern))
  end
  (expect_block["headersNotMatch"] || {}).each do |name, pattern|
    expect(headers[name.downcase].to_s).not_to match(Regexp.new(pattern))
  end

  # Parsed from the raw query rather than WebMock's normalized form, and compared
  # STRUCTURALLY: JSON key order differs per language and percent-encoding differs per
  # standard library, so comparing encoded strings would be flake by construction.
  query = CGI.parse(uri.query.to_s)
  (expect_block["queryJson"] || {}).each do |name, expected|
    raw = query[name]&.first
    expect(raw).not_to be_nil, -> { "query parameter #{name} is missing" }
    expect(JSON.parse(raw)).to eq(expected)
  end
  (expect_block["queryAbsent"] || []).each { |name| expect(query).not_to have_key(name) }
  (expect_block["queryJsonAbsentKeys"] || {}).each do |name, keys|
    actual = JSON.parse(query.fetch(name).first)
    keys.each { |key| expect(actual).not_to have_key(key) }
  end

  return unless expect_block.key?("bodyText")

  expect(signature.body.to_s).to eq(expect_block["bodyText"])
end

def run_response_case(given, expect_block)
  # Response cases queue one response and assert how it maps. Retry is the subject of
  # retry/*, and leaving it on would consume responses that do not exist for the 429 and
  # 5xx cases.
  call = { "source" => "zpl", "target" => "zpl", "bodyText" => "^XA^XZ" }
  outcome = run_fixture(call, [given], default_max_retries: 0)

  if expect_block.key?("error")
    assert_error(expect_block["error"], outcome[:error])
    return
  end

  expect(outcome[:error]).to be_nil, -> { "unexpected error: #{outcome[:error].inspect}" }
  result = outcome[:result]
  expected = expect_block["result"]

  expect(result.status).to eq(expected["status"]) if expected.key?("status")
  expect(result.content_type).to eq(expected["contentType"]) if expected.key?("contentType")
  expect(result.text).to eq(expected["text"]) if expected.key?("text")
  expect(result.request_id).to eq(expected["requestId"]) if expected.key?("requestId")
  return unless expected.key?("bytesBase64")

  expect([result.bytes].pack("m0")).to eq(expected["bytesBase64"])
end

def run_retry_case(given, expect_block)
  call = given.merge("source" => "zpl", "target" => "zpl", "bodyText" => "^XA^XZ")
  outcome = run_fixture(call, given["responses"])

  if expect_block.key?("error")
    assert_error(expect_block["error"], outcome[:error])
  else
    expect(outcome[:error]).to be_nil, -> { "unexpected error: #{outcome[:error].inspect}" }
    if expect_block.dig("result", "text")
      expect(outcome[:result].text).to eq(expect_block["result"]["text"])
    end
  end

  expect(outcome[:requests].length).to eq(expect_block["attempts"])
  expect(outcome[:sleeper].slept).to eq(expect_block["sleepsSeconds"])
end

def run_validation_case(given, expect_block)
  outcome = run_fixture(given, [OK_RESPONSE])
  error = outcome[:error]

  expect(error).to be_a(LabelZoom::ValidationError),
                   -> { "expected a ValidationError, got #{error.inspect}" }
  expect(error.parameter).to eq(expect_block.dig("validationError", "parameter"))
  # A local rejection is not an API error, so code rescuing APIError to implement a
  # fallback must not swallow it.
  expect(error).not_to be_a(LabelZoom::APIError)
  # Local validation must never reach the network.
  expect(outcome[:requests].length).to eq(expect_block["requestsSent"])
end

# ---------------------------------------------------------------------- the suite

RSpec.describe "conformance" do
  EXPECTED_CASE_IDS.each do |case_id|
    it case_id do
      fixture = JSON.parse((ROOT / "cases" / "#{case_id}.json").read)
      given = fixture["given"]
      expect_block = fixture["expect"]

      case case_id.split("/").first
      when "request" then run_request_case(given, expect_block)
      when "response" then run_response_case(given, expect_block)
      when "retry" then run_retry_case(given, expect_block)
      when "validation" then run_validation_case(given, expect_block)
      else raise "Unknown case kind for '#{case_id}'."
      end

      EXECUTED << case_id
    end
  end

  # The whole anti-drift mechanism. A suite that quietly runs a subset of the fixtures
  # reports success exactly like one that runs all of them, so coverage is asserted here
  # rather than assumed. Must run last -- see spec_helper's order: :defined.
  it "covers every declared case" do
    expect(SPEC["caseCount"]).to eq(ALL_CASE_IDS.length)

    SKIPS.each do |case_id, reason|
      expect(ALL_CASE_IDS).to include(case_id),
                              -> { "skips/#{LANGUAGE}.json declares unknown case '#{case_id}'" }
      expect(reason.strip).not_to be_empty, -> { "skip '#{case_id}' has no reason" }
    end

    expect(EXECUTED.sort).to eq(EXPECTED_CASE_IDS.sort)
  end
end
