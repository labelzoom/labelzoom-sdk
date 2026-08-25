# frozen_string_literal: true

require_relative "lib/labelzoom/version"

Gem::Specification.new do |spec|
  spec.name = "labelzoom"
  spec.version = LabelZoom::VERSION
  spec.authors = ["RJF Technology Solutions LLC"]
  spec.email = ["support@labelzoom.com"]

  spec.summary = "Official Ruby client for the LabelZoom label conversion API."
  spec.description = "Converts barcode labels between ZPL, EPL, TSPL, DPL, PDF, " \
                     "LabelZoom XML/JSON, and raster images via the LabelZoom API."
  spec.homepage = "https://www.labelzoom.com"
  spec.license = "MIT"
  spec.required_ruby_version = ">= 3.1"

  spec.metadata = {
    "homepage_uri" => spec.homepage,
    "source_code_uri" => "https://github.com/labelzoom/labelzoom-sdk",
    "documentation_uri" => "https://docs.labelzoom.com",
    "bug_tracker_uri" => "https://github.com/labelzoom/labelzoom-sdk/issues",
    "changelog_uri" => "https://github.com/labelzoom/labelzoom-sdk/releases",
    "rubygems_mfa_required" => "true"
  }

  # Enumerated rather than shelled out to `git ls-files`: this gemspec is evaluated from
  # the packaged gem too, where there is no git repository to ask.
  spec.files = Dir["lib/**/*.rb"] + ["LICENSE", "README.md"]
  spec.require_paths = ["lib"]

  # No runtime dependencies. net/http, json, uri and openssl are all standard library,
  # and this gem's most likely home is a Rails app that already pins its own HTTP stack.
end
