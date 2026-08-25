package labelzoom

import (
	"encoding/json"
	"fmt"
	"strings"
)

// maxMessageLength caps the derived message. APIError.RawBody keeps the whole body.
const maxMessageLength = 512

// APIError is the payload every error the LabelZoom API returns carries.
//
// It is embedded in the per-status types below, each of which unwraps to it, so both of
// these work:
//
//	var rateLimited *labelzoom.RateLimitedError
//	if errors.As(err, &rateLimited) { time.Sleep(rateLimited.RetryAfter()) }
//
//	var apiErr *labelzoom.APIError
//	if errors.As(err, &apiErr) { log.Printf("request %s failed: %s", apiErr.RequestID, apiErr.Message) }
//
// [ValidationError] deliberately does not unwrap to APIError: it is a bug in the calling
// code, not a server response, so code catching API errors to implement a fallback must not
// swallow it.
type APIError struct {
	// Status is the HTTP status code the API returned.
	Status int
	// Message is the human-readable detail, derived from the body and capped at 512
	// characters.
	Message string
	// RequestID is the X-LZ-Request-Id response header, when the server sent one. Quote it
	// to LabelZoom support -- it identifies the exact request server-side.
	RequestID string
	// RawBody is the response body, untruncated.
	RawBody string
}

func (e *APIError) Error() string {
	if e.RequestID != "" {
		return fmt.Sprintf("labelzoom: HTTP %d: %s (request %s)", e.Status, e.Message, e.RequestID)
	}
	return fmt.Sprintf("labelzoom: HTTP %d: %s", e.Status, e.Message)
}

// BadRequestError is HTTP 400: the request was malformed or the conversion path is invalid.
type BadRequestError struct{ APIError }

// Unwrap exposes the embedded [APIError] to errors.As.
func (e *BadRequestError) Unwrap() error { return &e.APIError }

// UnauthorizedError is HTTP 401: the supplied credential was rejected.
type UnauthorizedError struct{ APIError }

// Unwrap exposes the embedded [APIError] to errors.As.
func (e *UnauthorizedError) Unwrap() error { return &e.APIError }

// ForbiddenError is HTTP 403: the credential is valid but not entitled to this operation.
type ForbiddenError struct {
	APIError
	// IsPaidFeature is true when this 403 is a paywall rather than a permissions problem --
	// "JSON export is a paid feature" and friends. It is by far the most common
	// anonymous-tier failure, so it gets a flag instead of leaving callers matching strings.
	IsPaidFeature bool
}

// Unwrap exposes the embedded [APIError] to errors.As.
func (e *ForbiddenError) Unwrap() error { return &e.APIError }

// NotFoundError is HTTP 404: the conversion path does not exist.
type NotFoundError struct{ APIError }

// Unwrap exposes the embedded [APIError] to errors.As.
func (e *NotFoundError) Unwrap() error { return &e.APIError }

// PayloadTooLargeError is HTTP 413: the body exceeded the tier's limit, which is 1 MB on
// the anonymous free tier.
type PayloadTooLargeError struct{ APIError }

// Unwrap exposes the embedded [APIError] to errors.As.
func (e *PayloadTooLargeError) Unwrap() error { return &e.APIError }

// RateLimitedError is HTTP 429.
type RateLimitedError struct {
	APIError
	// RetryAfterSeconds is the Retry-After header, when the server sent one. The client
	// already honours it during its own retries; this exposes it for callers doing theirs.
	RetryAfterSeconds *float64
}

// Unwrap exposes the embedded [APIError] to errors.As.
func (e *RateLimitedError) Unwrap() error { return &e.APIError }

// ServerError is HTTP 5xx. Retried automatically before it surfaces.
type ServerError struct{ APIError }

// Unwrap exposes the embedded [APIError] to errors.As.
func (e *ServerError) Unwrap() error { return &e.APIError }

// ValidationError reports a request rejected locally, before any network call.
//
// Deliberately not an [APIError]: it carries no status, it is never retried, and it does not
// unwrap to APIError, so a caller inspecting API errors will not mistake it for one.
type ValidationError struct {
	// Parameter is the conversion parameter at fault, named as it appears on the wire.
	Parameter string
	Message   string
}

func (e *ValidationError) Error() string {
	return fmt.Sprintf("labelzoom: invalid %s: %s", e.Parameter, e.Message)
}

// errorForStatus maps a response onto the typed error for its status.
func errorForStatus(status int, message, requestID, rawBody string, retryAfter *float64) error {
	base := APIError{Status: status, Message: message, RequestID: requestID, RawBody: rawBody}

	switch {
	case status == 400:
		return &BadRequestError{APIError: base}
	case status == 401:
		return &UnauthorizedError{APIError: base}
	case status == 403:
		return &ForbiddenError{
			APIError:      base,
			IsPaidFeature: strings.Contains(strings.ToLower(message), "paid feature"),
		}
	case status == 404:
		return &NotFoundError{APIError: base}
	case status == 413:
		return &PayloadTooLargeError{APIError: base}
	case status == 429:
		return &RateLimitedError{APIError: base, RetryAfterSeconds: retryAfter}
	case status >= 500:
		return &ServerError{APIError: base}
	default:
		// Anything else non-2xx still becomes a typed error rather than being swallowed --
		// a 406 from content negotiation is the one that actually shows up in practice.
		return &base
	}
}

// extractMessage pulls the human-readable detail out of an error body.
//
// Both shapes in play put it on "message": the gateway returns {"message": "..."} and Spring
// returns {timestamp, status, error, message, path}. Anything else -- a rate-limit body keyed
// on "error", an HTML 502, a truncated JSON fragment -- falls through to the raw text, then
// to the status text.
func extractMessage(rawBody, statusText string) string {
	if strings.TrimSpace(rawBody) != "" {
		if message, ok := jsonMessage(rawBody); ok {
			return truncate(message)
		}
		return truncate(strings.TrimSpace(rawBody))
	}
	if strings.TrimSpace(statusText) != "" {
		return statusText
	}
	return "The LabelZoom API returned an error with no response body."
}

func truncate(value string) string {
	// Count runes, not bytes: cutting a multi-byte character in half would produce an
	// invalid string in an error message.
	runes := []rune(value)
	if len(runes) <= maxMessageLength {
		return value
	}
	return string(runes[:maxMessageLength])
}

// jsonMessage returns the "message" field of a JSON object body, if the body is a JSON
// object with a non-blank string there.
func jsonMessage(rawBody string) (string, bool) {
	var parsed map[string]any
	if err := json.Unmarshal([]byte(rawBody), &parsed); err != nil {
		// Not JSON, or malformed. The raw body is still the most useful thing available,
		// and a parse failure must not mask the HTTP error.
		return "", false
	}
	message, ok := parsed["message"].(string)
	if !ok || strings.TrimSpace(message) == "" {
		return "", false
	}
	return message, true
}
