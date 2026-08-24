# frozen_string_literal: true

require "json"
require "net/http"
require "openssl"
require "time"
require "uri"

module LabelZoom
  # Converts labels through the LabelZoom API.
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

    # Statuses worth retrying. Rule F1: nothing else, ever.
    RETRYABLE_STATUSES = ->(status) { status == 429 || status >= 500 }

    # E2's last resort, when a body is empty and the reason phrase is too. Net::HTTP does
    # not always surface a phrase, so this is derived from a table rather than the wire.
    REASON_PHRASES = {
      400 => "Bad Request", 401 => "Unauthorized", 403 => "Forbidden", 404 => "Not Found",
      406 => "Not Acceptable", 413 => "Payload Too Large", 429 => "Too Many Requests",
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
      headers = build_headers(source, base64_text)

      execute(uri, headers, body)
    end

    # The URL a given conversion would be posted to. Exposed because it is the first thing
    # anyone debugging a proxy or a base-URL override wants to see.
    def build_uri(source, target, params = nil)
      # Concatenated, not resolved: a base URL carrying a path prefix
      # (https://proxy.example.com/labelzoom) must keep it, which URI.join would discard.
      url = "#{@base_url}/api/v2/convert/#{Formats.source_token(source)}/to/" \
            "#{Formats.target_token(target)}"
      # Rule C7: no options means a bare URL, not an empty query string.
      url += "?#{URI.encode_www_form("params" => params)}" unless params.nil?
      URI.parse(url)
    end

    private

    def resolve_credential(api_key, env)
      if UNSET.equal?(api_key)
        value = env[API_KEY_ENVIRONMENT_VARIABLE]
        return value.nil? || value.empty? ? nil : value
      end

      # An explicit nil or "" forces anonymous and must not fall back to the environment.
      api_key.nil? || api_key.empty? ? nil : api_key
    end

    def build_headers(source, base64_text)
      headers = {
        "Content-Type" => base64_text ? "text/plain" : Formats.media_type(source),
        # Accept must be */*. The server's `produces` list omits image/gif, image/bmp and
        # image/jpeg, so naming the target's exact media type yields a 406 from content
        # negotiation before the handler ever runs.
        "Accept" => "*/*",
        "User-Agent" => @user_agent
      }
      headers["Authorization"] = "Bearer #{@credential}" unless @credential.nil?
      headers
    end

    def execute(uri, headers, body)
      attempts = @max_retries + 1

      (1..).each do |attempt|
        begin
          response = perform(uri, headers, body)
        rescue *transport_exceptions => e
          if attempt >= attempts
            raise TransportError, "labelzoom: request to #{uri} failed: #{e.message}"
          end

          delay(attempt, nil)
          next
        end

        status = response.code.to_i
        return build_result(response, status) if status.between?(200, 299)

        retry_after = retry_after_seconds(response)
        if attempt >= attempts || !RETRYABLE_STATUSES.call(status)
          raise error_for(response, status, retry_after)
        end

        delay(attempt, retry_after)
      end
    end

    def perform(uri, headers, body)
      http = @http_builder&.call(uri) || Net::HTTP.new(uri.host, uri.port)
      http.use_ssl = uri.scheme == "https"
      http.open_timeout = @timeout
      http.read_timeout = @timeout

      request = Net::HTTP::Post.new(uri)
      headers.each { |name, value| request[name] = value }
      request.body = body

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

    def error_for(response, status, retry_after)
      raw_body = (response.body || "").dup.force_encoding(Encoding::BINARY)
      message = extract_message(raw_body, REASON_PHRASES[status])
      common = { status: status, request_id: response[REQUEST_ID_HEADER], raw_body: raw_body }

      case status
      when 400 then BadRequestError.new(message, **common)
      when 401 then UnauthorizedError.new(message, **common)
      when 403
        ForbiddenError.new(message, paid_feature: message.match?(/paid feature/i), **common)
      when 404 then NotFoundError.new(message, **common)
      when 413 then PayloadTooLargeError.new(message, **common)
      when 429 then RateLimitedError.new(message, retry_after_seconds: retry_after, **common)
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
