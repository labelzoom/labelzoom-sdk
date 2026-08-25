package labelzoom_test

import (
	"bytes"
	"encoding/base64"
	"errors"
	"fmt"
	"io"
	"net"
	"net/http"
	"strings"
	"sync"
	"time"
)

// roundTripperFunc adapts a function to http.RoundTripper. This is the seam
// docs/CONFORMANCE.md prescribes for Go: no socket is ever opened, so the suite passes
// identically on a fork pull request with no secrets and no network.
type roundTripperFunc func(*http.Request) (*http.Response, error)

func (f roundTripperFunc) RoundTrip(r *http.Request) (*http.Response, error) { return f(r) }

// scriptedResponse is one entry of a fixture's given.responses.
type scriptedResponse struct {
	Status         int               `json:"status"`
	Headers        map[string]string `json:"headers"`
	BodyText       string            `json:"bodyText"`
	BodyBase64     string            `json:"bodyBase64"`
	BodyTextRepeat []any             `json:"bodyTextRepeat"`
	TransportError string            `json:"transportError"`
}

func (s scriptedResponse) body() ([]byte, error) {
	switch {
	case s.BodyBase64 != "":
		return base64.StdEncoding.DecodeString(s.BodyBase64)
	case len(s.BodyTextRepeat) == 2:
		fill, _ := s.BodyTextRepeat[0].(string)
		count, _ := s.BodyTextRepeat[1].(float64)
		return []byte(strings.Repeat(fill, int(count))), nil
	default:
		return []byte(s.BodyText), nil
	}
}

// recordedRequest is a request captured by the stub, with its body already drained.
//
// The body has to be read here rather than in the assertion phase: http.Request.Body is a
// one-shot stream, and reading it later would return nothing.
type recordingTransport struct {
	mu       sync.Mutex
	script   []scriptedResponse
	index    int
	requests []*http.Request
	bodies   [][]byte
}

func newRecordingTransport(script []scriptedResponse) *recordingTransport {
	return &recordingTransport{script: script}
}

func (t *recordingTransport) client() *http.Client {
	return &http.Client{Transport: roundTripperFunc(t.roundTrip)}
}

func (t *recordingTransport) roundTrip(request *http.Request) (*http.Response, error) {
	t.mu.Lock()
	defer t.mu.Unlock()

	var body []byte
	if request.Body != nil {
		body, _ = io.ReadAll(request.Body)
		_ = request.Body.Close()
	}
	t.requests = append(t.requests, request)
	t.bodies = append(t.bodies, body)

	if t.index >= len(t.script) {
		return nil, fmt.Errorf("unexpected request to %s; no more responses are scripted", request.URL)
	}
	scripted := t.script[t.index]
	t.index++

	if scripted.TransportError != "" {
		// A dial failure, which rule F1 makes retryable. net.OpError is what the real
		// transport returns, so the client sees the same shape it would in production.
		return nil, &net.OpError{
			Op:  "dial",
			Net: "tcp",
			Err: errors.New(scripted.TransportError + ": connection refused"),
		}
	}

	payload, err := scripted.body()
	if err != nil {
		return nil, err
	}

	header := http.Header{}
	for name, value := range scripted.Headers {
		header.Set(name, value)
	}

	return &http.Response{
		Status:     fmt.Sprintf("%d %s", scripted.Status, http.StatusText(scripted.Status)),
		StatusCode: scripted.Status,
		Header:     header,
		Body:       io.NopCloser(bytes.NewReader(payload)),
		Request:    request,
	}, nil
}

func (t *recordingTransport) count() int {
	t.mu.Lock()
	defer t.mu.Unlock()
	return len(t.requests)
}

// last returns the most recent request and its drained body.
func (t *recordingTransport) last() (*http.Request, []byte) {
	t.mu.Lock()
	defer t.mu.Unlock()
	if len(t.requests) == 0 {
		return nil, nil
	}
	return t.requests[len(t.requests)-1], t.bodies[len(t.bodies)-1]
}

// recordingSleeper records what the retry backoff asked for instead of waiting. Rule F4
// makes this seam a requirement: a suite that really slept would cost ten seconds of CI
// time per language.
type recordingSleeper struct {
	mu    sync.Mutex
	slept []time.Duration
}

func (s *recordingSleeper) sleep(d time.Duration) {
	s.mu.Lock()
	defer s.mu.Unlock()
	s.slept = append(s.slept, d)
}

func (s *recordingSleeper) seconds() []float64 {
	s.mu.Lock()
	defer s.mu.Unlock()
	out := make([]float64, 0, len(s.slept))
	for _, d := range s.slept {
		out = append(out, d.Seconds())
	}
	return out
}

// recordingEnv is the injected environment lookup. Passing one rather than reading the real
// process environment keeps a developer's own LABELZOOM_API_KEY out of the result -- and it
// lets a case assert the SDK did NOT consult the environment, which t.Setenv cannot.
type recordingEnv struct {
	values  map[string]string
	lookups []string
}

func (e *recordingEnv) lookup(key string) (string, bool) {
	e.lookups = append(e.lookups, key)
	value, ok := e.values[key]
	return value, ok
}
