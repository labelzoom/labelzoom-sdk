package labelzoom

import (
	"net/http"
	"os"
	"time"
)

// Option configures a [Client]. See [New].
type Option func(*config)

type config struct {
	// apiKey is a pointer so that "never configured" is distinguishable from "explicitly
	// anonymous". The first reads the environment; the second must not.
	apiKey          *string
	baseURL         string
	maxRetries      int
	timeout         time.Duration
	userAgentSuffix string
	httpClient      *http.Client
	useJitter       bool
	sleep           func(time.Duration)
	env             func(string) (string, bool)
}

func defaultConfig() config {
	return config{
		baseURL:    DefaultBaseURL,
		maxRetries: 2,
		httpClient: &http.Client{},
		useJitter:  true,
		sleep:      time.Sleep,
		env:        os.LookupEnv,
	}
}

// WithAPIKey sets the credential: an lz_live_/lz_test_ key or a JWT.
//
// Passing an empty string forces anonymous mode and suppresses the LABELZOOM_API_KEY
// fallback, which is what you want when a config file may or may not carry a key and you
// do not want the environment deciding. See also [WithAnonymous].
//
// Without this option the client reads LABELZOOM_API_KEY from the environment.
func WithAPIKey(apiKey string) Option {
	return func(c *config) { c.apiKey = &apiKey }
}

// WithAnonymous forces the free tier: no credential, and no environment fallback.
//
// The anonymous tier is watermarked, converts the first label only, caps requests at 1 MB,
// and rejects multi-page, JSON-target and image-to-image conversions.
func WithAnonymous() Option {
	return func(c *config) {
		empty := ""
		c.apiKey = &empty
	}
}

// WithBaseURL overrides the API host. A path prefix is preserved, so a reverse proxy at
// https://proxy.example.com/labelzoom works.
func WithBaseURL(baseURL string) Option {
	return func(c *config) { c.baseURL = baseURL }
}

// WithMaxRetries sets the number of retries after the initial attempt. Defaults to 2, for
// 3 attempts in total. Zero disables retrying.
func WithMaxRetries(maxRetries int) Option {
	return func(c *config) { c.maxRetries = maxRetries }
}

// WithTimeout sets a per-attempt timeout. Zero, the default, means no client-side timeout
// beyond whatever the context and the http.Client impose.
func WithTimeout(timeout time.Duration) Option {
	return func(c *config) { c.timeout = timeout }
}

// WithUserAgentSuffix appends a token to the SDK's own User-Agent.
//
// It is appended, never prepended: the server parses a leading "LabelZoomStudio/" as a
// Studio version and silently changes PDF handling for versions <= 1.8.2.
func WithUserAgentSuffix(suffix string) Option {
	return func(c *config) { c.userAgentSuffix = suffix }
}

// WithHTTPClient substitutes the http.Client used for every request. This is the seam to
// stub in tests -- give it a Transport that returns canned responses and no socket is ever
// opened.
func WithHTTPClient(httpClient *http.Client) Option {
	return func(c *config) { c.httpClient = httpClient }
}

// WithSleeper replaces the delay between retries. Substitute a recording no-op in tests so
// the retry paths cost no wall-clock time.
func WithSleeper(sleep func(time.Duration)) Option {
	return func(c *config) { c.sleep = sleep }
}

// WithoutJitter makes the retry backoff exactly 1s, 2s, 4s instead of a random duration up
// to that bound. For deterministic tests; leave jitter on in production, where it is what
// stops a fleet retrying in lockstep.
func WithoutJitter() Option {
	return func(c *config) { c.useJitter = false }
}

// WithEnvLookup replaces the environment lookup used to find LABELZOOM_API_KEY. Injecting
// it keeps a developer's real key out of a test's outcome.
func WithEnvLookup(lookup func(string) (string, bool)) Option {
	return func(c *config) { c.env = lookup }
}
