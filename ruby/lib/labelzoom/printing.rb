# frozen_string_literal: true

require "securerandom"
require "time"

module LabelZoom
  # A print job the server accepted, returned by {Client#print}.
  #
  # @!attribute [r] job_id
  #   @return [String] the job's id. Pass it to +client.jobs.get+ to follow the job.
  # @!attribute [r] status
  #   @return [String] +"dispatched"+ when an online agent took the job, +"queued"+ when none
  #     is online. Kept as a String: the set of statuses can grow, and a new one must not
  #     fail the call.
  # @!attribute [r] copies
  #   @return [Integer, nil] the copies requested, or nil when the request set none.
  # @!attribute [r] idempotent
  #   @return [Boolean] true when the request reused an Idempotency-Key: this is the
  #     original job, and nothing printed again.
  PrintJob = Struct.new(:job_id, :status, :copies, :idempotent, keyword_init: true) do
    def idempotent? = idempotent == true
  end

  # The jobs a template print created, one per record, returned by {Client#print_template}.
  #
  # @!attribute [r] jobs
  #   @return [Array<PrintJob>]
  # @!attribute [r] remaining
  #   @return [Integer] records at the end of the batch that were not submitted. Send the
  #     same request with the same idempotency key to submit them; records already submitted
  #     replay without printing again.
  # @!attribute [r] message
  #   @return [String, nil] the server's explanation when +remaining+ is not 0.
  PrintBatch = Struct.new(:jobs, :remaining, :message, keyword_init: true)

  # A print job's current state, returned by +client.jobs.get+. The wire is snake_case, so
  # the members are named exactly as the API names them.
  #
  # @!attribute [r] status
  #   @return [String] +queued+, +dispatched+, +printing+, +completed+ or +failed+. Kept as a
  #     String, because the set of statuses can grow.
  # @!attribute [r] error
  #   @return [String, nil] why the job failed.
  # @!attribute [r] source_format
  #   @return [String, nil] the format the job was stored in: the printer's language after
  #     any conversion.
  # The four +*_date+ members are +Time+ objects, each nil when unset.
  PrintJobDetails = Struct.new(:id, :printer_id, :agent_id, :source_format, :copies, :status,
                               :error, :retry_count, :created_date, :dispatched_date,
                               :completed_date, :retried_date, keyword_init: true)

  # One counted quota. +pending+ is set for agents only, and +reset_at+ (a +Time+) for jobs
  # today only.
  PrintQuotaCounter = Struct.new(:used, :limit, :remaining, :pending, :reset_at,
                                 keyword_init: true)

  # The size limits on a print request.
  PrintLimits = Struct.new(:max_payload_bytes, :max_stored_payload_bytes, :max_batch_size,
                           keyword_init: true)

  # The account's Cloud Print limits and today's usage, returned by {Client#print_quota}.
  # +jobs_today+ resets at +jobs_today.reset_at+, 00:00 UTC.
  PrintQuota = Struct.new(:plan, :agents, :printers, :jobs_today, :limits, keyword_init: true)

  # Print jobs, from +client.jobs+. Shaped as its own object so that +client.printers+ and
  # +client.templates+ can sit beside it later without changing anything here.
  class PrintJobs
    # @api private -- use +client.jobs+.
    def initialize(fetch)
      @fetch = fetch
    end

    # Gets a job's current state. Poll it to follow a job to +completed+ or +failed+.
    #
    # @param job_id [String] the +job_id+ of a {PrintJob}.
    # @return [PrintJobDetails]
    # @raise [ValidationError] if +job_id+ is empty, before any network call.
    # @raise [NotFoundError] if the job does not exist.
    def get(job_id) = @fetch.call(job_id)
  end

  # The print calls' validation and response reading, written once for every print call.
  #
  # @api private
  module Printing
    MIN_COPIES = 1
    MAX_COPIES = 9999

    module_function

    # @return [String] the id, unchanged.
    # @raise [ValidationError] named +parameter+, when the id is missing or blank.
    def require_id!(value, parameter)
      return value if value.is_a?(String) && !value.strip.empty?

      raise ValidationError.new(parameter, "#{parameter} cannot be empty.")
    end

    # @return [Integer, nil] the copies, or nil when the caller set none.
    def copies!(copies)
      return nil if copies.nil?
      return copies if copies.is_a?(Integer) && copies.between?(MIN_COPIES, MAX_COPIES)

      raise ValidationError.new("copies",
                                "copies must be a whole number from #{MIN_COPIES} to " \
                                "#{MAX_COPIES}, but was #{copies.inspect}.")
    end

    # The Idempotency-Key for one call. Fixed before the first attempt, so every retry of
    # the call sends the same key (contract P4): without it, a retry after a lost response
    # could print the label twice.
    #
    # @return [String] the caller's key exactly as given, else a random lowercase UUID v4.
    def key_for_call(key)
      return SecureRandom.uuid if key.nil?
      return key if key.is_a?(String) && !key.strip.empty?

      raise ValidationError.new("idempotencyKey",
                                "An idempotency key cannot be blank. Omit it to have the SDK " \
                                "generate one.")
    end

    # The JSON body of a template print: one record as that object, several as an array,
    # and none as an empty object (contract P3).
    def template_body(records)
      payload =
        case records.length
        when 0 then {}
        when 1 then records.first
        else records
        end
      JSON.generate(payload)
    end

    def read_job(json)
      PrintJob.new(job_id: json_string(json, "jobId"), status: json_string(json, "status"),
                   copies: json_integer(json, "copies"), idempotent: json["idempotent"] == true)
    end

    # One record comes back as a bare job and several as +{"jobs": [...]}+. Callers get one
    # shape either way.
    def read_batch(json)
      unless json["jobs"].is_a?(Array)
        return PrintBatch.new(jobs: [read_job(json)], remaining: 0, message: nil)
      end

      jobs = json["jobs"].map { |job| read_job(job.is_a?(Hash) ? job : {}) }
      PrintBatch.new(jobs: jobs, remaining: json_integer(json, "remaining") || 0,
                     message: json_string(json, "message"))
    end

    def read_job_details(json)
      PrintJobDetails.new(
        id: json_string(json, "id"),
        printer_id: json_string(json, "printer_id"),
        agent_id: json_string(json, "agent_id"),
        source_format: json_string(json, "source_format"),
        copies: json_integer(json, "copies"),
        status: json_string(json, "status"),
        error: json_string(json, "error"),
        retry_count: json_integer(json, "retry_count") || 0,
        created_date: json_instant(json, "created_date"),
        dispatched_date: json_instant(json, "dispatched_date"),
        completed_date: json_instant(json, "completed_date"),
        retried_date: json_instant(json, "retried_date")
      )
    end

    def read_quota(json)
      limits = json_object(json, "limits")
      PrintQuota.new(
        plan: json_string(json, "plan"),
        agents: read_counter(json_object(json, "agents")),
        printers: read_counter(json_object(json, "printers")),
        jobs_today: read_counter(json_object(json, "jobsToday")),
        limits: PrintLimits.new(
          max_payload_bytes: json_integer(limits, "maxPayloadBytes"),
          max_stored_payload_bytes: json_integer(limits, "maxStoredPayloadBytes"),
          max_batch_size: json_integer(limits, "maxBatchSize")
        )
      )
    end

    def read_counter(json)
      PrintQuotaCounter.new(used: json_integer(json, "used"), limit: json_integer(json, "limit"),
                            remaining: json_integer(json, "remaining"),
                            pending: json_integer(json, "pending"),
                            reset_at: json_instant(json, "resetAt"))
    end

    def json_string(json, name) = json[name].is_a?(String) ? json[name] : nil

    def json_integer(json, name) = json[name].is_a?(Integer) ? json[name] : nil

    def json_object(json, name) = json[name].is_a?(Hash) ? json[name] : {}

    def json_instant(json, name)
      text = json_string(json, name)
      text.nil? ? nil : Time.iso8601(text)
    rescue ArgumentError
      # An unparseable timestamp is not worth failing an otherwise good response.
      nil
    end
  end
end
