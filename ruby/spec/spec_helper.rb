# frozen_string_literal: true

require "labelzoom"
require "webmock/rspec"

RSpec.configure do |config|
  config.expect_with(:rspec) { |expectations| expectations.syntax = :expect }
  config.mock_with(:rspec) { |mocks| mocks.verify_partial_doubles = true }
  config.shared_context_metadata_behavior = :apply_to_host_groups
  config.disable_monkey_patching!

  # The completeness assertion in conformance_spec.rb depends on running AFTER the
  # generated per-case examples, in the same process. Do not add a shuffling or parallel
  # runner without reworking it -- the same warning python/pyproject.toml carries.
  config.order = :defined
end

# No socket is ever opened: every request is answered by a WebMock stub, so the suite
# passes identically on a fork pull request with no secrets.
WebMock.disable_net_connect!(allow_localhost: false)
