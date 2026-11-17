# frozen_string_literal: true

require "json"
require "net/http"
require "openssl"
require "time"
require "uri"

module LabelZoom
  # Converts labels through the LabelZoom API, and prints them through Cloud Print.
  #
  # Authentication is optional. Without a credential the API serves a free tier:
  # watermarked output, the first label only, a 1 MB request cap, and no multi-page,
  # JSON-target or image-to-image conversion. Constructing a client with no key is
  # therefore a supported, tested path rather than an error.
  class Client
    # The production API host.
    DEFAULT_BASE_URL = "https://api.labelzoom.com"

    # The environment variable consulted when no credential is configured.
    API_KEY_ENVIRONMENT_VARIABLE = "LABELZOOM_API_KEY"

    # The support handle the gateway stamps on every response. Net::HTTP looks headers up
    # case-insensitively, which matters here: the gateway sets X-LZ-Request-Id but CORS
    # exposes it as X-LZ-Request-ID.
    REQUEST_ID_HEADER = "x-lz-request-id"

    # E2 caps the derived message here. APIError#raw_body keeps the whole body.
    MAX_MESSAGE_LENGTH = 512

    # Every print call's Accept, and a template print's Content-Type: every print success
    # response is JSON, so convert's */* (rule B2) does not apply.
    JSON_MEDIA_TYPE = "application/json"

    # The 429 code that makes a {PrintQuotaExceededError} (rule E4), which is never retried.
    DAILY_JOB_LIMIT_CODE = "daily_job_limit_reached"

    # Statuses worth retrying. Rule F1: nothing else, ever.
    RETRYABLE_STATUSES = ->(status) { status == 429 || status >= 500 }

    # E2's last resort, when a body is empty and the reason phrase is too. Net::HTTP does
    # not always surface a phrase, so this is derived from a table rather than the wire.
    REASON_PHRASES = {
      400 => "Bad Request", 401 => "Unauthorized", 403 => "Forbidden", 404 => "Not Found",
      406 => "Not Acceptable", 413 => "Payload Too Large", 422 => "Unprocessable Content",
      429 => "Too Many Requests",
      500 => "Internal Server Error", 502 => "Bad Gateway", 503 => "Service Unavailable",
      504 => "Gateway Timeout"
    }.freeze

    # @param api_key [String, nil, LabelZoom::UNSET] an lz_live_/lz_test_ key or a JWT.
    #   Left at +UNSET+ the client reads +LABELZOOM_API_KEY+ from +env+. Passing +nil+ or
    #   an empty String forces anonymous mode and suppresses that fallback.
    # @param base_url [String] a path prefix is preserved, so a reverse proxy at
    #   https://proxy.example.com/labelzoom works.
    # @param max_retries [Integer] retries after the initial attempt. 0 disables retrying.
    # @param timeout [Numeric] per-attempt read and open timeout, in seconds.
    # @param user_agent_suffix [String, nil] appended to the SDK's own User-Agent.
    # @param sleeper [#call, nil] replaces the delay between retries. Substitute a
    #   recording no-op in tests so the retry paths cost no wall-clock time.
    # @param jitter [Boolean] full jitter on the retry backoff. Turn it off for
    #   deterministic tests; leave it on in production, where it is what stops a fleet
    #   retrying in lockstep.
    # @param env [#[]] the environment lookup. Injecting it keeps a developer's real key
    #   out of a test's outcome.
    # @param http_builder [#call, nil] builds the Net::HTTP instance. An escape hatch for
    #   proxies and custom TLS; the conformance suite uses WebMock instead.
    def initialize(api_key: UNSET, base_url: DEFAULT_BASE_URL, max_retries: 2, timeout: 60,
                   user_agent_suffix: nil, sleeper: nil, jitter: true, env: ENV,
                   http_builder: nil)
      raise ArgumentError, "max_retries cannot be negative" if max_retries.negative?

      # All trailing slashes, not one: a base URL of "https://api.labelzoom.com///" must
      # still produce a single-slash path.
      @base_url = base_url.to_s.sub(%r{/+\z}, "")
      @credential = resolve_credential(api_key, env)
      @max_retries = max_retries
      @timeout = timeout
      @sleeper = sleeper || ->(seconds) { Kernel.sleep(seconds) }
      @jitter = jitter
      @http_builder = http_builder

      # The server parses a "LabelZoomStudio/" User-Agent prefix as a Studio version and
      # silently changes PDF handling for versions <= 1.8.2, so the SDK's own token must
      # come first.
      suffix = user_agent_suffix.to_s.strip
      @user_agent = "labelzoom-ruby-sdk/#{VERSION} (ruby/#{RUBY_VERSION})"
      @user_agent += " #{suffix}" unless suffix.empty?
    end

    # Whether a credential was resolved. False means requests go out on the anonymous free
    # tier, which is a supported mode rather than an error.
    def authenticated? = !@credential.nil?

    # Runs one conversion.
    #
    # @param source [Symbol] the format of +body+.
    # @param target [Symbol] the format to produce.
    # @param body [String] the document. For +:url+ it is the URL to fetch, as text.
    # @param base64_text [Boolean] send +body+ as base64 +text/plain+ rather than the
    #   source's own media type. Only the binary sources support it.
    # @param options [Hash] conversion parameters, as nested keyword arguments --
    #   +label: { width: 4, height: 6 }+ rather than Python's flattened +label_width+.
    #   Only what you pass is sent; the SDK never fills in a client-side default, so a
    #   change to a server default reaches you without a gem upgrade.
    #
    #   +label+ is in INCHES, not dots, and omitting it entirely asks the server to detect
    #   the size. +pdf: { page_number: }+ is 0-BASED; omit it to convert every page.
    #
    # @return [ConversionResult]
    # @raise [ValidationError] if the request is rejected locally, before any network call.
    # @raise [APIError] on any non-2xx response.
    def convert(source, target, body, base64_text: false, **options)
      source = Formats.source!(source)
      target = Formats.target!(target)
      body = body.to_s

      if body.empty?
        # The gateway rejects a zero-length body with 400; catching it here saves a round
        # trip and gives a clearer message.
        raise ValidationError.new("body",
                                  "Source body cannot be empty; the API rejects zero-length " \
                                  "requests.")
      end

      params = Options.serialize(**options)
      uri = build_uri(source, target, params)
      # Accept must be */*. The server's `produces` list omits image/gif, image/bmp and
      # image/jpeg, so naming the target's exact media type yields a 406 from content
      # negotiation before the handler ever runs.
      headers = request_headers(
        "*/*", "Content-Type" => base64_text ? "text/plain" : Formats.media_type(source)
      )

      execute(:post, uri, headers, body) { |response, status| build_result(response, status) }
    end

    # Prints a document on a Cloud Print printer. Printing needs an API key with the +print+
    # scope.
    #
    # There is no target format: the printer's configuration picks the output language, and
    # LabelZoom converts the document to it, so a PDF prints on a ZPL printer.
    #
    # @param printer_id [String] the printer to print on.
    # @param source [Symbol] the format of +body+: one of {PRINT_SOURCE_FORMATS}. Print
    #   converts only those; convert anything else to the printer's language first.
    # @param body [String] the document.
    # @param copies [Integer, nil] sends the whole job this many times, 1 to 9999. It
    #   multiplies any quantity in the document: +^PQ3+ with 3 copies prints 9 labels.
    # @param idempotency_key [String, nil] makes the print safe to repeat: the same key
    #   returns the original job instead of printing again. Use something unique to the
    #   label, such as an order number. Omitted, the SDK generates one per call, so its own
    #   retries cannot print twice.
    # @param source_options [Hash] how the document is read: the source options {#convert}
    #   takes, in the same nested form (+label:+, +pdf:+, +split:+, +source_dpi:+, ...).
    #   Target options such as +target_dpi:+ raise, because the printer picks the output.
    #
    # @return [PrintJob]
    # @raise [ValidationError] if the request is rejected locally, before any network call.
    # @raise [APIError] on any non-2xx response.
    def print(printer_id, source, body, copies: nil, idempotency_key: nil, **source_options)
      printer_id = Printing.require_id!(printer_id, "printerId")
      source = Formats.print_source!(source)
      body = body.to_s
      if body.empty?
        raise ValidationError.new("body",
                                  "The document cannot be empty; the API rejects an empty print " \
                                  "request.")
      end
      copies = Printing.copies!(copies)
      key = Printing.key_for_call(idempotency_key)
      # An empty params would make the server convert a document that is already in the
      # printer's language, so it is sent only when a source option is set (rule P2).
      params = Options.serialize_source(**source_options)

      # sourceFormat on every request: the server cannot tell ZPL from EPL by Content-Type.
      query = [["sourceFormat", Formats.source_token(source)]]
      query << ["copies", copies] unless copies.nil?
      query << ["params", params] unless params.nil?
      uri = api_uri("/api/v3/printers/#{encode_segment(printer_id)}/print", query)
      headers = request_headers(JSON_MEDIA_TYPE, "Content-Type" => Formats.media_type(source),
                                                 "Idempotency-Key" => key)

      execute(:post, uri, headers, body) { |response, _| Printing.read_job(read_json(response)) }
    end

    # Prints a published template, one label per record.
    #
    # @param printer_id [String] the printer to print on.
    # @param template_id [String] a template published from LabelZoom Studio or the web
    #   designer.
    # @param data [Hash, Array<Hash>, nil] one Hash of merge-field values per label. A single
    #   Hash is one label. Omit it for a template with no merge fields.
    # @param validate [Boolean] checks every record for missing merge fields before anything
    #   prints.
    # @param copies [Integer, nil] sends each label this many times, 1 to 9999.
    # @param idempotency_key [String, nil] one key for the whole batch; the server derives
    #   each record's key from it.
    #
    # @return [PrintBatch] a batch whatever the number of records. If +remaining+ is not 0,
    #   send the same request with the same key to submit the rest.
    # @raise [ValidationError] if the request is rejected locally, before any network call.
    # @raise [APIError] on any non-2xx response.
    def print_template(printer_id, template_id, data: nil, validate: false, copies: nil,
                       idempotency_key: nil)
      printer_id = Printing.require_id!(printer_id, "printerId")
      template_id = Printing.require_id!(template_id, "templateId")
      records = data.nil? ? [] : Options.normalize_data(data)
      copies = Printing.copies!(copies)
      key = Printing.key_for_call(idempotency_key)

      query = []
      query << ["copies", copies] unless copies.nil?
      query << %w[validate true] if validate
      uri = api_uri("/api/v3/printers/#{encode_segment(printer_id)}/templates/" \
                    "#{encode_segment(template_id)}/print", query)
      headers = request_headers(JSON_MEDIA_TYPE, "Content-Type" => JSON_MEDIA_TYPE,
                                                 "Idempotency-Key" => key)

      execute(:post, uri, headers, Printing.template_body(records)) do |response, _|
        Printing.read_batch(read_json(response))
      end
    end

    # Print jobs: +client.jobs.get(job_id)+ follows one to completion.
    #
    # @return [PrintJobs]
    def jobs
      @jobs ||= PrintJobs.new(->(job_id) { fetch_job(job_id) })
    end

    # The account's Cloud Print limits and today's usage. Check it to stop before the daily
    # job limit's {PrintQuotaExceededError}.
    #
    # @return [PrintQuota]
    # @raise [APIError] on any non-2xx response.
    def print_quota
      execute(:get, api_uri("/api/v3/print/quota"), request_headers(JSON_MEDIA_TYPE), nil) do |r, _|
        Printing.read_quota(read_json(r))
      end
    end

    # The URL a given conversion would be posted to. Exposed because it is the first thing
    # anyone debugging a proxy or a base-URL override wants to see.
    def build_uri(source, target, params = nil)
      api_uri("/api/v2/convert/#{Formats.source_token(source)}/to/" \
              "#{Formats.target_token(target)}",
              params.nil? ? [] : [["params", params]])
    end

    private

    # Concatenated, not resolved: a base URL carrying a path prefix
    # (https://proxy.example.com/labelzoom) must keep it, which URI.join would discard.
    # Rule C7: no query parameters means a bare URL, not an empty query string.
    def api_uri(path, query = [])
      url = "#{@base_url}#{path}"
      url += "?#{URI.encode_www_form(query)}" unless query.empty?
      URI.parse(url)
    end

    # Percent-encodes an id as one path segment (rule P1): everything but RFC 3986's
    # unreserved characters, byte by byte.
    def encode_segment(value)
      value.b.gsub(/[^A-Za-z0-9\-._~]/n) { |byte| format("%%%02X", byte.ord) }
    end

    def fetch_job(job_id)
      job_id = Printing.require_id!(job_id, "jobId")
      uri = api_uri("/api/v3/jobs/#{encode_segment(job_id)}")
      execute(:get, uri, request_headers(JSON_MEDIA_TYPE), nil) do |response, _|
        Printing.read_job_details(read_json(response))
      end
    end

    def resolve_credential(api_key, env)
      if UNSET.equal?(api_key)
        value = env[API_KEY_ENVIRONMENT_VARIABLE]
        return value.nil? || value.empty? ? nil : value
      end

      # An explicit nil or "" forces anonymous and must not fall back to the environment.
      api_key.nil? || api_key.empty? ? nil : api_key
    end

    def request_headers(accept, extra = {})
      headers = extra.merge("Accept" => accept, "User-Agent" => @user_agent)
      headers["Authorization"] = "Bearer #{@credential}" unless @credential.nil?
      headers
    end

    # Sends one request under the retry policy (rule F) and yields the 2xx response to the
    # block, whose value is returned. Every call goes through here, convert and print alike.
    # The headers, Idempotency-Key included, are fixed before the first attempt, so every
    # retry sends the same ones.
    def execute(method, uri, headers, body)
      attempts = @max_retries + 1

      (1..).each do |attempt|
        begin
          response = perform(method, uri, headers, body)
        rescue *transport_exceptions => e
          if attempt >= attempts
            raise TransportError, "labelzoom: request to #{uri} failed: #{e.message}"
          end

          delay(attempt, nil)
          next
        end

        status = response.code.to_i
        return yield(response, status) if status.between?(200, 299)

        retry_after = retry_after_seconds(response)
        error = error_for(response, status, retry_after)
        # Rule F5: the daily print limit's Retry-After is the 00:00 UTC reset, hours away, and
        # honoring it would hang the call.
        if attempt >= attempts || !RETRYABLE_STATUSES.call(status) ||
           error.is_a?(PrintQuotaExceededError)
          raise error
        end

        delay(attempt, retry_after)
      end
    end

    def perform(method, uri, headers, body)
      http = @http_builder&.call(uri) || Net::HTTP.new(uri.host, uri.port)
      http.use_ssl = uri.scheme == "https"
      http.open_timeout = @timeout
      http.read_timeout = @timeout

      request = (method == :get ? Net::HTTP::Get : Net::HTTP::Post).new(uri)
      headers.each { |name, value| request[name] = value }
      # The GETs send no body, and so no Content-Type (rule P1).
      request.body = body unless body.nil?

      http.start { |session| session.request(request) }
    end

    def transport_exceptions
      [SocketError, SystemCallError, Net::OpenTimeout, Net::ReadTimeout, IOError,
       OpenSSL::SSL::SSLError]
    end

    def build_result(response, status)
      ConversionResult.new(
        bytes: (response.body || "").dup.force_encoding(Encoding::BINARY),
        status: status,
        content_type: response["content-type"],
        request_id: response[REQUEST_ID_HEADER]
      )
    end

    # Every print success response is a JSON object.
    def read_json(response)
      parsed = JSON.parse((response.body || "").dup.force_encoding(Encoding::UTF_8))
      parsed.is_a?(Hash) ? parsed : {}
    rescue JSON::ParserError => e
      raise Error, "labelzoom: the API returned a success response that is not JSON: #{e.message}"
    end

    def error_for(response, status, retry_after)
      raw_body = (response.body || "").dup.force_encoding(Encoding::BINARY)
      message = extract_message(raw_body, REASON_PHRASES[status])
      code = extract_code(raw_body)
      common = { status: status, request_id: response[REQUEST_ID_HEADER], raw_body: raw_body,
                 code: code }

      case status
      when 400 then BadRequestError.new(message, **common)
      when 401 then UnauthorizedError.new(message, **common)
      when 403
        ForbiddenError.new(message, paid_feature: message.match?(/paid feature/i), **common)
      when 404 then NotFoundError.new(message, **common)
      when 413 then PayloadTooLargeError.new(message, **common)
      when 422 then UnprocessableError.new(message, **common)
      when 429
        type = code == DAILY_JOB_LIMIT_CODE ? PrintQuotaExceededError : RateLimitedError
        type.new(message, retry_after_seconds: retry_after, **common)
      when 500.. then ServerError.new(message, **common)
      else
        # Anything else non-2xx still becomes a typed error rather than being swallowed --
        # a 406 from content negotiation is the one that actually shows up in practice.
        APIError.new(message, **common)
      end
    end

    # Both shapes in play put the detail on "message": the gateway returns
    # {"message": "..."} and Spring returns {timestamp, status, error, message, path}.
    # Anything else -- a rate-limit body keyed on "error", an HTML 502, a truncated JSON
    # fragment -- falls through to the raw text, then to the reason phrase.
    def extract_message(raw_body, reason_phrase)
      text = raw_body.dup.force_encoding(Encoding::UTF_8).scrub
      unless text.strip.empty?
        begin
          parsed = JSON.parse(text)
          detail = parsed["message"] if parsed.is_a?(Hash)
          return truncate(detail) if detail.is_a?(String) && !detail.strip.empty?
        rescue JSON::ParserError
          # Not JSON, or malformed. The raw body is still the most useful thing available,
          # and a parse failure must not mask the HTTP error.
        end
        return truncate(text.strip)
      end

      return reason_phrase if reason_phrase && !reason_phrase.strip.empty?

      "The LabelZoom API returned an error with no response body."
    end

    # Rule E3: the body's top-level "code" string, else nil. Never a reason to fail.
    def extract_code(raw_body)
      parsed = JSON.parse(raw_body.dup.force_encoding(Encoding::UTF_8).scrub)
      code = parsed["code"] if parsed.is_a?(Hash)
      code.is_a?(String) ? code : nil
    rescue JSON::ParserError
      nil
    end

    def truncate(value)
      value.length <= MAX_MESSAGE_LENGTH ? value : value[0, MAX_MESSAGE_LENGTH]
    end

    # Read from the RESPONSE, on any retryable status. Deliberately not read off the typed
    # 429 error: RFC 9110 allows Retry-After on a 503 too, and a 503 asking for 5 seconds
    # must not be retried after a 1-second backoff.
    def retry_after_seconds(response)
      header = response["retry-after"]
      return nil if header.nil? || header.strip.empty?

      begin
        return Float(header.strip)
      rescue ArgumentError
        # Not a number; try the HTTP-date form below.
      end

      begin
        [0.0, Time.httpdate(header) - Time.now].max.ceil.to_f
      rescue ArgumentError
        nil
      end
    end

    # 1s, 2s, 4s with full jitter, overridden by a longer Retry-After.
    def delay(attempt, retry_after)
      backoff = 2.0**(attempt - 1)
      wait = @jitter ? backoff * Kernel.rand : backoff

      # The server knows better than the backoff curve when it tells us how long to wait.
      wait = retry_after if retry_after && retry_after > wait

      @sleeper.call(wait)
    end
  end
end
