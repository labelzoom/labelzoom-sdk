# frozen_string_literal: true

module LabelZoom
  # Base class for everything this gem raises on its own behalf.
  class Error < StandardError; end

  # Base class for every error the LabelZoom API returns.
  #
  # Rescue this to handle them all. Note that {ValidationError} deliberately does *not*
  # descend from it.
  class APIError < Error
    # @return [Integer] the HTTP status code the API returned.
    attr_reader :status

    # @return [String, nil] the X-LZ-Request-Id response header, if present. Quote this to
    #   LabelZoom support -- it identifies the exact request server-side.
    attr_reader :request_id

    # @return [String] the raw response body, untruncated. {#message} is derived from it
    #   and capped at 512 characters; this is not.
    attr_reader :raw_body

    def initialize(message, status:, request_id: nil, raw_body: "")
      super(message)
      @status = status
      @request_id = request_id
      @raw_body = raw_body
    end
  end

  # HTTP 400. The request was malformed or the conversion path is invalid.
  class BadRequestError < APIError; end

  # HTTP 401. The supplied credential was rejected.
  class UnauthorizedError < APIError; end

  # HTTP 403. The credential is valid but not entitled to this operation.
  class ForbiddenError < APIError
    def initialize(message, paid_feature: false, **kwargs)
      super(message, **kwargs)
      @paid_feature = paid_feature
    end

    # True when this 403 is a paywall rather than a permissions problem -- "JSON export is
    # a paid feature" and friends. By far the most common anonymous-tier failure, so it
    # gets a predicate instead of leaving callers to match strings.
    def paid_feature? = @paid_feature
  end

  # HTTP 404. The conversion path does not exist.
  class NotFoundError < APIError; end

  # HTTP 413. The body exceeded the tier's limit -- 1 MB on the anonymous free tier.
  class PayloadTooLargeError < APIError; end

  # HTTP 429. Too many requests.
  class RateLimitedError < APIError
    # @return [Float, nil] Retry-After in seconds, when the server sent one. The client
    #   already honours this during its own retries; this exposes it for callers doing
    #   their own.
    attr_reader :retry_after_seconds

    def initialize(message, retry_after_seconds: nil, **kwargs)
      super(message, **kwargs)
      @retry_after_seconds = retry_after_seconds
    end
  end

  # HTTP 5xx. Retried automatically before surfacing.
  class ServerError < APIError; end

  # A transport-level failure: the request never got a response.
  class TransportError < Error; end

  # A request rejected locally, before any network call.
  #
  # Deliberately an ArgumentError and *not* an {APIError}: this is a bug in the calling
  # code, not a server response. It carries no status, it is never retried, and a caller
  # rescuing {APIError} to implement fallback behaviour should not swallow it.
  #
  # This is also what makes conformance/skips/ruby.json's stated reason literally true:
  # Ruby has no compile step, so a source-only format passed as a target is caught here.
  class ValidationError < ArgumentError
    # @return [String] the conversion parameter at fault, named as it appears on the wire.
    attr_reader :parameter

    def initialize(parameter, message)
      super(message)
      @parameter = parameter
    end
  end
end
