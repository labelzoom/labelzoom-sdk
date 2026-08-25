# frozen_string_literal: true

# Records what the retry backoff asked for instead of waiting.
#
# Rule F4 makes this seam a requirement rather than a convenience: a suite that really
# slept would cost ten seconds of CI time per language.
class RecordingSleeper
  attr_reader :slept

  def initialize = @slept = []

  def call(seconds) = @slept << seconds
end
