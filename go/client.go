package labelzoom

import (
	"bytes"
	"context"
	"errors"
	"fmt"
	"io"
	"math"
	"math/rand/v2"
	"net/http"
	"net/url"
	"runtime"
	"strconv"
	"strings"
	"time"
)

// Client converts labels through the LabelZoom API. It is safe for concurrent use.
type Client struct {
	baseURL       string
	credential    string
	hasCredential bool
	maxRetries    int
	timeout       time.Duration
	userAgent     string
	httpClient    *http.Client
	useJitter     bool
	sleep         func(time.Duration)
}

// ConvertRequest describes one conversion.
//
// Go gets a request struct rather than the fluent chain the other SDKs expose: a chain in
// Go has to either panic or defer its errors to a terminal Do(), and both are un-Go. The
// wire behaviour is identical -- only the ergonomics differ.
type ConvertRequest struct {
	// From is the format of Body.
	From SourceFormat
	// To is the format to produce.
	To TargetFormat
	// Body is the document. For [SourceURL] it is the URL to fetch, as text.
	Body []byte
	// Options are the conversion parameters. Nil sends none, which is a bare URL with no
	// query string at all.
	Options *Options
	// AsBase64Text sends Body as base64 text/plain rather than the source's own media
	// type. Only the binary sources (PDF and the raster images) support it.
	AsBase64Text bool
}

// New builds a client.
//
// Every argument is optional: with no options at all the client reads LABELZOOM_API_KEY
// from the environment, falls back to the anonymous free tier if it is unset, and talks to
// [DefaultBaseURL].
func New(options ...Option) (*Client, error) {
	cfg := defaultConfig()
	for _, apply := range options {
		apply(&cfg)
	}

	if cfg.maxRetries < 0 {
		return nil, fmt.Errorf("labelzoom: maxRetries cannot be negative, got %d", cfg.maxRetries)
	}

	credential, hasCredential := resolveCredential(cfg)

	userAgent := fmt.Sprintf("labelzoom-go-sdk/%s (%s)", Version, runtime.Version())
	if suffix := strings.TrimSpace(cfg.userAgentSuffix); suffix != "" {
		userAgent += " " + suffix
	}

	return &Client{
		// All trailing slashes, not one: a base URL of "https://api.labelzoom.com///"
		// must still produce a single-slash path.
		baseURL:       strings.TrimRight(cfg.baseURL, "/"),
		credential:    credential,
		hasCredential: hasCredential,
		maxRetries:    cfg.maxRetries,
		timeout:       cfg.timeout,
		userAgent:     userAgent,
		httpClient:    cfg.httpClient,
		useJitter:     cfg.useJitter,
		sleep:         cfg.sleep,
	}, nil
}

// IsAuthenticated reports whether a credential was resolved. False means requests go out on
// the anonymous free tier, which is a supported mode rather than an error.
func (c *Client) IsAuthenticated() bool { return c.hasCredential }

func resolveCredential(cfg config) (string, bool) {
	if cfg.apiKey == nil {
		value, ok := cfg.env(APIKeyEnvVar)
		if !ok || value == "" {
			return "", false
		}
		return value, true
	}
	// An explicit empty string forces anonymous and must not fall back to the environment.
	if *cfg.apiKey == "" {
		return "", false
	}
	return *cfg.apiKey, true
}

// Convert runs one conversion.
//
// On a non-2xx response it returns one of the typed errors in this package -- see
// [APIError]. A request rejected before it leaves the process returns a [ValidationError],
// which does not unwrap to [APIError].
func (c *Client) Convert(ctx context.Context, request ConvertRequest) (*Result, error) {
	if len(request.Body) == 0 {
		// The gateway rejects a zero-length body with 400; catching it here saves a round
		// trip and gives a clearer message.
		return nil, &ValidationError{
			Parameter: "body",
			Message:   "Source body cannot be empty; the API rejects zero-length requests.",
		}
	}

	if err := request.To.validate(); err != nil {
		return nil, err
	}
	mediaType, err := request.From.mediaType()
	if err != nil {
		return nil, err
	}
	if request.AsBase64Text {
		mediaType = "text/plain"
	}

	params, err := request.Options.serialize()
	if err != nil {
		return nil, err
	}

	// Concatenated, not resolved: a base URL carrying a path prefix
	// (https://proxy.example.com/labelzoom) must keep it, which url.URL.ResolveReference
	// would discard.
	endpoint := fmt.Sprintf("%s/api/v2/convert/%s/to/%s",
		c.baseURL, request.From.wireToken(), request.To.wireToken())

	query := url.Values{}
	if params != "" {
		query.Set("params", params)
	}
	if request.Options != nil {
		for key, value := range request.Options.RawQuery {
			query.Set(key, value)
		}
	}
	if len(query) > 0 {
		// Rule C7: no options means a bare URL, not an empty query string.
		endpoint += "?" + query.Encode()
	}

	attempts := c.maxRetries + 1
	for attempt := 1; ; attempt++ {
		response, err := c.attempt(ctx, endpoint, mediaType, request.Body)
		if err != nil {
			// A cancelled or expired context is the caller's decision, not a transport
			// failure, and must not be retried.
			if ctx.Err() != nil || attempt >= attempts {
				return nil, err
			}
			c.delay(attempt, nil)
			continue
		}

		if response.StatusCode >= 200 && response.StatusCode < 300 {
			return readResult(response)
		}

		apiErr, retryAfter := readError(response)
		if attempt >= attempts || !isRetryable(response.StatusCode) {
			return nil, apiErr
		}
		c.delay(attempt, retryAfter)
	}
}

func (c *Client) attempt(ctx context.Context, endpoint, mediaType string, body []byte) (*http.Response, error) {
	if c.timeout > 0 {
		var cancel context.CancelFunc
		ctx, cancel = context.WithTimeout(ctx, c.timeout)
		defer cancel()
	}

	// A fresh reader per attempt: the body has to be replayable, and a consumed one would
	// make every retry send zero bytes.
	request, err := http.NewRequestWithContext(ctx, http.MethodPost, endpoint, bytes.NewReader(body))
	if err != nil {
		return nil, fmt.Errorf("labelzoom: could not build the request: %w", err)
	}
	request.ContentLength = int64(len(body))

	request.Header.Set("Content-Type", mediaType)
	// Accept must be */*. The server's `produces` list omits image/gif, image/bmp and
	// image/jpeg, so naming the target's exact media type yields a 406 from content
	// negotiation before the handler ever runs.
	request.Header.Set("Accept", "*/*")
	request.Header.Set("User-Agent", c.userAgent)
	if c.hasCredential {
		request.Header.Set("Authorization", "Bearer "+c.credential)
	}

	response, err := c.httpClient.Do(request)
	if err != nil {
		return nil, fmt.Errorf("labelzoom: request to %s failed: %w", endpoint, err)
	}
	return response, nil
}

// delay waits 1s, 2s, 4s with full jitter, overridden by a longer Retry-After.
func (c *Client) delay(attempt int, retryAfter *float64) {
	backoff := time.Duration(math.Pow(2, float64(attempt-1)) * float64(time.Second)) //nolint:gosec // bounded by maxRetries
	wait := backoff
	if c.useJitter {
		wait = time.Duration(rand.Float64() * float64(backoff))
	}

	// The server knows better than the backoff curve when it tells us how long to wait.
	if retryAfter != nil {
		if server := time.Duration(*retryAfter * float64(time.Second)); server > wait {
			wait = server
		}
	}

	c.sleep(wait)
}

func isRetryable(status int) bool { return status == http.StatusTooManyRequests || status >= 500 }

func readResult(response *http.Response) (*Result, error) {
	defer func() { _ = response.Body.Close() }()

	body, err := io.ReadAll(response.Body)
	if err != nil {
		return nil, fmt.Errorf("labelzoom: could not read the conversion result: %w", err)
	}

	return &Result{
		Bytes:       body,
		ContentType: response.Header.Get("Content-Type"),
		Status:      response.StatusCode,
		// Header.Get canonicalizes, which matters here: the gateway sets X-LZ-Request-Id
		// but CORS exposes it as X-LZ-Request-ID.
		RequestID: response.Header.Get(requestIDHeader),
	}, nil
}

func readError(response *http.Response) (error, *float64) {
	defer func() { _ = response.Body.Close() }()

	// A body that cannot be read must not mask the HTTP error.
	raw, _ := io.ReadAll(response.Body)
	rawBody := string(raw)

	retryAfter := retryAfterSeconds(response)
	message := extractMessage(rawBody, statusText(response))

	return errorForStatus(
		response.StatusCode,
		message,
		response.Header.Get(requestIDHeader),
		rawBody,
		retryAfter,
	), retryAfter
}

// statusText is the HTTP reason phrase, which extractMessage falls back to when the body is
// empty. net/http keeps the server's own phrase on Response.Status ("406 Not Acceptable");
// a hand-built response in a test may not have one, so derive it from the code instead.
func statusText(response *http.Response) string {
	if _, phrase, found := strings.Cut(response.Status, " "); found && strings.TrimSpace(phrase) != "" {
		return phrase
	}
	return http.StatusText(response.StatusCode)
}

// retryAfterSeconds reads Retry-After from the RESPONSE, on any retryable status.
//
// Deliberately not read off the typed 429 error: RFC 9110 allows Retry-After on a 503 too,
// and a 503 asking for 5 seconds must not be retried after a 1-second backoff.
func retryAfterSeconds(response *http.Response) *float64 {
	header := response.Header.Get("Retry-After")
	if header == "" {
		return nil
	}

	if seconds, err := strconv.ParseFloat(strings.TrimSpace(header), 64); err == nil {
		return &seconds
	}

	if when, err := http.ParseTime(header); err == nil {
		seconds := math.Max(0, math.Ceil(time.Until(when).Seconds()))
		return &seconds
	}
	return nil
}

// errValidation is a convenience for callers doing errors.Is against the local-validation
// class as a whole rather than reaching for its Parameter.
var errValidation = errors.New("labelzoom: local validation failed")

// Is lets errors.Is(err, ErrValidation) match any [ValidationError].
func (e *ValidationError) Is(target error) bool { return target == errValidation }

// ErrValidation matches any [ValidationError] under errors.Is, for callers that only need
// to tell a local rejection from a server response.
var ErrValidation = errValidation
