// Runs the shared conformance fixtures against the Go SDK.
//
// Entirely offline: the http.Client's Transport is a recording stub, so this passes
// identically on a fork pull request with no secrets. The fixtures are the same ones the
// .NET, Node, Java, Python and PHP suites run -- see docs/CONFORMANCE.md.
package labelzoom_test

import (
	"context"
	"encoding/base64"
	"encoding/json"
	"errors"
	"fmt"
	"math"
	"net/url"
	"os"
	"path/filepath"
	"reflect"
	"regexp"
	"sort"
	"strings"
	"testing"

	labelzoom "github.com/labelzoom/labelzoom-sdk/go"
)

const language = "go"

type spec struct {
	Version    string   `json:"version"`
	CaseCount  int      `json:"caseCount"`
	Cases      []string `json:"cases"`
	ErrorKinds []string `json:"errorKinds"`
}

type skipFile struct {
	Language string `json:"language"`
	Skips    []struct {
		ID     string `json:"id"`
		Reason string `json:"reason"`
	} `json:"skips"`
}

type fixture struct {
	ID     string          `json:"id"`
	Note   string          `json:"note"`
	Given  json.RawMessage `json:"given"`
	Expect json.RawMessage `json:"expect"`
}

type given struct {
	// Raw so that an absent "apiKey" stays distinguishable from an explicit null: the
	// first means "read the environment" and the second means "force anonymous", and
	// rule G2 turns entirely on that difference.
	Client         map[string]json.RawMessage `json:"client"`
	Env            map[string]string          `json:"env"`
	Source         string                     `json:"source"`
	Target         string                     `json:"target"`
	BodyText       string                     `json:"bodyText"`
	SourceEncoding string                     `json:"sourceEncoding"`
	Options        map[string]any             `json:"options"`
	MaxRetries     *int                       `json:"maxRetries"`
	Responses      []scriptedResponse         `json:"responses"`
	Snippet        string                     `json:"snippet"`

	// Response cases inline a single response at the top level rather than in responses[].
	Status     int               `json:"status"`
	Headers    map[string]string `json:"headers"`
	BodyBase64 string            `json:"bodyBase64"`
	BodyRepeat []any             `json:"bodyTextRepeat"`
}

type expectation struct {
	Method             string                      `json:"method"`
	URL                string                      `json:"url"`
	Path               string                      `json:"path"`
	Headers            map[string]string           `json:"headers"`
	HeadersAbsent      []string                    `json:"headersAbsent"`
	HeadersMatch       map[string]string           `json:"headersMatch"`
	HeadersNotMatch    map[string]string           `json:"headersNotMatch"`
	QueryJSON          map[string]any              `json:"queryJson"`
	QueryAbsent        []string                    `json:"queryAbsent"`
	QueryJSONAbsentKey map[string][]string         `json:"queryJsonAbsentKeys"`
	BodyText           *string                     `json:"bodyText"`
	Result             *expectedResult             `json:"result"`
	Error              *expectedError              `json:"error"`
	Attempts           *int                        `json:"attempts"`
	SleepsSeconds      []float64                   `json:"sleepsSeconds"`
	ValidationError    *struct{ Parameter string } `json:"validationError"`
	RequestsSent       *int                        `json:"requestsSent"`
	CompileError       bool                        `json:"compileError"`
}

type expectedResult struct {
	Status      *int
	ContentType *string
	Text        *string
	BytesBase64 *string
	RequestID   *string
	// requestIDPresent separates an omitted "requestId" from an explicit null. The null
	// form is an assertion in its own right: response/request-id-absent requires the SDK
	// to surface no request id rather than throwing (rule D2).
	requestIDPresent bool
}

func (r *expectedResult) UnmarshalJSON(data []byte) error {
	var raw map[string]json.RawMessage
	if err := json.Unmarshal(data, &raw); err != nil {
		return err
	}
	read := func(key string, into any) error {
		value, present := raw[key]
		if !present {
			return nil
		}
		return json.Unmarshal(value, into)
	}
	for key, into := range map[string]any{
		"status": &r.Status, "contentType": &r.ContentType, "text": &r.Text,
		"bytesBase64": &r.BytesBase64, "requestId": &r.RequestID,
	} {
		if err := read(key, into); err != nil {
			return fmt.Errorf("expect.result.%s: %w", key, err)
		}
	}
	_, r.requestIDPresent = raw["requestId"]
	return nil
}

type expectedError struct {
	Kind              *string  `json:"kind"`
	Status            *int     `json:"status"`
	Message           *string  `json:"message"`
	MessageNonEmpty   bool     `json:"messageNonEmpty"`
	MessageMaxLength  *int     `json:"messageMaxLength"`
	RawBodyLength     *int     `json:"rawBodyLength"`
	RawBodyPresent    bool     `json:"rawBodyPresent"`
	RequestID         *string  `json:"requestId"`
	IsPaidFeature     *bool    `json:"isPaidFeature"`
	RetryAfterSeconds *float64 `json:"retryAfterSeconds"`
}

// okResponse is what a request/* case gets back: the assertions are about what went out.
var okResponse = scriptedResponse{
	Status:   200,
	Headers:  map[string]string{"content-type": "text/plain"},
	BodyText: "^XA^XZ",
}

func findConformanceRoot(t *testing.T) string {
	t.Helper()
	directory, err := os.Getwd()
	if err != nil {
		t.Fatalf("could not determine the working directory: %v", err)
	}
	for {
		candidate := filepath.Join(directory, "conformance")
		if info, err := os.Stat(filepath.Join(candidate, "cases")); err == nil && info.IsDir() {
			return candidate
		}
		parent := filepath.Dir(directory)
		if parent == directory {
			t.Fatal("could not locate the conformance/ directory")
		}
		directory = parent
	}
}

func readJSON(t *testing.T, path string, into any) {
	t.Helper()
	raw, err := os.ReadFile(path) //nolint:gosec // paths come from the fixture manifest
	if err != nil {
		t.Fatalf("could not read %s: %v", path, err)
	}
	if err := json.Unmarshal(raw, into); err != nil {
		t.Fatalf("could not parse %s: %v", path, err)
	}
}

// TestConformance executes every case the spec declares, minus this language's declared
// skips, and then asserts it really did execute all of them.
func TestConformance(t *testing.T) {
	root := findConformanceRoot(t)

	var declared spec
	readJSON(t, filepath.Join(root, "spec.json"), &declared)

	skips := map[string]string{}
	skipPath := filepath.Join(root, "skips", language+".json")
	if _, err := os.Stat(skipPath); err == nil {
		var file skipFile
		readJSON(t, skipPath, &file)
		if file.Language != language {
			t.Fatalf("skips/%s.json declares language %q", language, file.Language)
		}
		for _, skip := range file.Skips {
			skips[skip.ID] = skip.Reason
		}
	}

	var expected []string
	for _, id := range declared.Cases {
		if _, skipped := skips[id]; !skipped {
			expected = append(expected, id)
		}
	}

	executed := map[string]bool{}
	for _, caseID := range expected {
		// Sequential on purpose: t.Parallel() here would race on `executed` and buy nothing,
		// since every case is CPU-bound string work against a stub.
		t.Run(caseID, func(t *testing.T) {
			runCase(t, root, caseID)
			executed[caseID] = true
		})
	}

	// The whole anti-drift mechanism. A suite that quietly runs a subset of the fixtures
	// reports success exactly like one that runs all of them, so coverage is asserted here
	// rather than assumed.
	t.Run("covers every declared case", func(t *testing.T) {
		if declared.CaseCount != len(declared.Cases) {
			t.Errorf("spec.json caseCount is %d but it lists %d cases",
				declared.CaseCount, len(declared.Cases))
		}
		for id, reason := range skips {
			if !contains(declared.Cases, id) {
				t.Errorf("skips/%s.json declares unknown case %q", language, id)
			}
			if strings.TrimSpace(reason) == "" {
				t.Errorf("skip %q has no reason", id)
			}
		}

		var ran []string
		for id := range executed {
			ran = append(ran, id)
		}
		sort.Strings(ran)
		want := append([]string(nil), expected...)
		sort.Strings(want)

		if !reflect.DeepEqual(ran, want) {
			t.Errorf("executed %d of %d expected cases;\nmissing: %v",
				len(ran), len(want), missing(want, ran))
		}
	})
}

func runCase(t *testing.T, root, caseID string) {
	t.Helper()

	var loaded fixture
	readJSON(t, filepath.Join(root, "cases", caseID+".json"), &loaded)

	var g given
	if err := json.Unmarshal(loaded.Given, &g); err != nil {
		t.Fatalf("could not parse given: %v", err)
	}
	var e expectation
	if err := json.Unmarshal(loaded.Expect, &e); err != nil {
		t.Fatalf("could not parse expect: %v", err)
	}

	switch kind, _, _ := strings.Cut(caseID, "/"); kind {
	case "request":
		runRequestCase(t, g, e)
	case "response":
		runResponseCase(t, g, e)
	case "retry":
		runRetryCase(t, g, e)
	case "validation":
		runValidationCase(t, g, e)
	case "typecheck":
		runTypecheckCase(t, caseID, g)
	default:
		t.Fatalf("unknown case kind for %q", caseID)
	}
}

// ------------------------------------------------------------------- translation

// buildOptions translates the fixture's wire-shaped options into the SDK's Options struct.
//
// This translation layer is the only per-language code in a runner, and writing it is what
// proves the Go divergence in API_CONTRACT.md section 9 is skin deep.
func buildOptions(t *testing.T, wire map[string]any) *labelzoom.Options {
	t.Helper()
	if len(wire) == 0 {
		return nil
	}

	options := &labelzoom.Options{}
	for key, value := range wire {
		switch key {
		case "dpi":
			options.DPI = labelzoom.Ptr(int(value.(float64)))
		case "rotation":
			options.Rotation = labelzoom.Ptr(int(value.(float64)))
		case "scaling":
			options.Scaling = labelzoom.Ptr(value.(float64))
		case "colorMode":
			options.ColorMode = labelzoom.Ptr(labelzoom.ColorMode(value.(string)))
		case "darkness":
			options.Darkness = labelzoom.Ptr(int(value.(float64)))
		case "position":
			position := value.(map[string]any)
			options.Position = &labelzoom.Position{
				X: int(position["x"].(float64)),
				Y: int(position["y"].(float64)),
			}
		case "watermark":
			options.Watermark = labelzoom.Ptr(value.(bool))
		case "dialect":
			options.Dialect = labelzoom.Ptr(value.(string))
		case "data":
			options.Data = value
		case "label":
			size := value.(map[string]any)
			options.Label = &labelzoom.LabelSize{}
			if width, ok := size["width"].(float64); ok {
				options.Label.Width = labelzoom.Ptr(width)
			}
			if height, ok := size["height"].(float64); ok {
				options.Label.Height = labelzoom.Ptr(height)
			}
		case "pdf":
			pdf := value.(map[string]any)
			options.PDF = &labelzoom.PDFOptions{}
			if mode, ok := pdf["conversionMode"].(string); ok {
				options.PDF.ConversionMode = labelzoom.Ptr(labelzoom.PDFConversionMode(mode))
			}
			if page, ok := pdf["pageNumber"].(float64); ok {
				options.PDF.PageNumber = labelzoom.Ptr(int(page))
			}
		case "zpl":
			zpl := value.(map[string]any)
			options.ZPL = &labelzoom.ZPLOptions{}
			if commands, ok := zpl["commandsToIgnore"].([]any); ok {
				for _, command := range commands {
					options.ZPL.CommandsToIgnore = append(options.ZPL.CommandsToIgnore, command.(string))
				}
			}
			if compression, ok := zpl["imageCompression"].(string); ok {
				options.ZPL.ImageCompression = labelzoom.Ptr(labelzoom.ZPLImageCompression(compression))
			}
		default:
			t.Fatalf("fixture sets option %q, which the Go runner does not map. "+
				"Add it to buildOptions rather than skipping the case.", key)
		}
	}
	return options
}

// execute runs one fixture through the SDK and returns everything the assertions need.
func execute(t *testing.T, g given, script []scriptedResponse, defaultMaxRetries *int) (
	*labelzoom.Result, error, *recordingTransport, *recordingSleeper, *recordingEnv,
) {
	t.Helper()

	transport := newRecordingTransport(script)
	sleeper := &recordingSleeper{}
	env := &recordingEnv{values: map[string]string{}}
	for name, value := range g.Env {
		env.values[name] = value
	}

	options := []labelzoom.Option{
		labelzoom.WithHTTPClient(transport.client()),
		labelzoom.WithSleeper(sleeper.sleep),
		labelzoom.WithEnvLookup(env.lookup),
		// The fixtures assert exact sleep durations, so the backoff has to be deterministic.
		labelzoom.WithoutJitter(),
	}

	if g.Client == nil {
		// A case with no client block is not exercising credential resolution. Force
		// anonymous so a stray LABELZOOM_API_KEY cannot change the outcome.
		options = append(options, labelzoom.WithAnonymous())
	} else if raw, present := g.Client["apiKey"]; present {
		// Present-and-null means "no credential"; omitting the key entirely means
		// "read the environment", which is the case that must NOT call WithAPIKey.
		var apiKey *string
		if err := json.Unmarshal(raw, &apiKey); err != nil {
			t.Fatalf("could not parse given.client.apiKey: %v", err)
		}
		if apiKey == nil {
			options = append(options, labelzoom.WithAnonymous())
		} else {
			options = append(options, labelzoom.WithAPIKey(*apiKey))
		}
	}
	if raw, present := g.Client["baseUrl"]; present {
		var baseURL string
		if err := json.Unmarshal(raw, &baseURL); err != nil {
			t.Fatalf("could not parse given.client.baseUrl: %v", err)
		}
		options = append(options, labelzoom.WithBaseURL(baseURL))
	}

	maxRetries := defaultMaxRetries
	if g.MaxRetries != nil {
		maxRetries = g.MaxRetries
	}
	if maxRetries != nil {
		options = append(options, labelzoom.WithMaxRetries(*maxRetries))
	}

	client, err := labelzoom.New(options...)
	if err != nil {
		t.Fatalf("could not construct the client: %v", err)
	}

	request := labelzoom.ConvertRequest{
		From:         labelzoom.SourceFormat(g.Source),
		To:           labelzoom.TargetFormat(g.Target),
		Body:         []byte(g.BodyText),
		Options:      buildOptions(t, g.Options),
		AsBase64Text: g.SourceEncoding == "base64text",
	}

	result, err := client.Convert(context.Background(), request)
	return result, err, transport, sleeper, env
}

// ------------------------------------------------------------------ case runners

func runRequestCase(t *testing.T, g given, e expectation) {
	t.Helper()

	_, err, transport, _, env := execute(t, g, []scriptedResponse{okResponse}, nil)
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}

	request, body := transport.last()
	if request == nil {
		t.Fatal("no request was sent")
	}

	if e.Method != "" && request.Method != e.Method {
		t.Errorf("method = %q, want %q", request.Method, e.Method)
	}
	if e.URL != "" {
		actual := request.URL.Scheme + "://" + request.URL.Host + request.URL.Path
		if actual != e.URL {
			t.Errorf("url = %q, want %q", actual, e.URL)
		}
	}
	if e.Path != "" && request.URL.Path != e.Path {
		t.Errorf("path = %q, want %q", request.URL.Path, e.Path)
	}

	// http.Header.Get canonicalizes the name, which is the case-insensitive semantics the
	// fixtures assume. expect.headers is a subset assertion; headersAbsent is exact.
	for name, want := range e.Headers {
		if got := request.Header.Get(name); got != want {
			t.Errorf("header %s = %q, want %q", name, got, want)
		}
	}
	for _, name := range e.HeadersAbsent {
		if values := request.Header.Values(name); len(values) > 0 {
			t.Errorf("header %s must be absent, got %v", name, values)
		}
	}
	for name, pattern := range e.HeadersMatch {
		if !regexp.MustCompile(pattern).MatchString(request.Header.Get(name)) {
			t.Errorf("header %s = %q, want match %q", name, request.Header.Get(name), pattern)
		}
	}
	for name, pattern := range e.HeadersNotMatch {
		if regexp.MustCompile(pattern).MatchString(request.Header.Get(name)) {
			t.Errorf("header %s = %q, want NO match for %q", name, request.Header.Get(name), pattern)
		}
	}

	query, err := url.ParseQuery(request.URL.RawQuery)
	if err != nil {
		t.Fatalf("could not parse the query string %q: %v", request.URL.RawQuery, err)
	}

	for name, want := range e.QueryJSON {
		raw := query.Get(name)
		if raw == "" {
			t.Fatalf("query parameter %s is missing", name)
		}
		var got any
		if err := json.Unmarshal([]byte(raw), &got); err != nil {
			t.Fatalf("query parameter %s is not JSON: %v", name, err)
		}
		// Structural, not textual: JSON key order differs per language (Go randomizes map
		// iteration outright) and percent-encoding differs per standard library, so
		// comparing encoded strings would be flake by construction. Both sides come out of
		// encoding/json, so every number is a float64 and 4 compares equal to 4.0.
		if !reflect.DeepEqual(got, want) {
			t.Errorf("query %s = %#v, want %#v", name, got, want)
		}
	}
	for _, name := range e.QueryAbsent {
		if _, present := query[name]; present {
			t.Errorf("query parameter %s must be absent", name)
		}
	}
	for name, keys := range e.QueryJSONAbsentKey {
		var got map[string]any
		if err := json.Unmarshal([]byte(query.Get(name)), &got); err != nil {
			t.Fatalf("query parameter %s is not a JSON object: %v", name, err)
		}
		for _, key := range keys {
			if _, present := got[key]; present {
				t.Errorf("%s.%s must not be serialized", name, key)
			}
		}
	}

	if e.BodyText != nil && string(body) != *e.BodyText {
		t.Errorf("body = %q, want %q", string(body), *e.BodyText)
	}

	// Rule G2's negative half: an explicitly anonymous client must not consult the
	// environment at all. No other language's suite can assert this, because they read the
	// real process environment rather than an injected lookup.
	if raw, present := g.Client["apiKey"]; present && string(raw) == `""` && len(env.lookups) > 0 {
		t.Errorf("an empty API key must force anonymous without an environment lookup, "+
			"but the SDK looked up %v", env.lookups)
	}
}

func runResponseCase(t *testing.T, g given, e expectation) {
	t.Helper()

	// Response cases queue one response and assert how it maps. Retry is the subject of
	// retry/*, and leaving it on would consume responses that do not exist for the 429 and
	// 5xx cases.
	scripted := scriptedResponse{
		Status:         g.Status,
		Headers:        g.Headers,
		BodyText:       g.BodyText,
		BodyBase64:     g.BodyBase64,
		BodyTextRepeat: g.BodyRepeat,
	}
	call := given{Source: "zpl", Target: "zpl", BodyText: "^XA^XZ"}

	result, err, _, _, _ := execute(t, call, []scriptedResponse{scripted}, labelzoom.Ptr(0))

	if e.Error != nil {
		assertError(t, *e.Error, err)
		return
	}
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}

	want := e.Result
	if want.Status != nil && result.Status != *want.Status {
		t.Errorf("status = %d, want %d", result.Status, *want.Status)
	}
	if want.ContentType != nil && result.ContentType != *want.ContentType {
		t.Errorf("contentType = %q, want %q", result.ContentType, *want.ContentType)
	}
	if want.Text != nil && result.Text() != *want.Text {
		t.Errorf("text = %q, want %q", result.Text(), *want.Text)
	}
	if want.BytesBase64 != nil {
		got := base64.StdEncoding.EncodeToString(result.Bytes)
		if got != *want.BytesBase64 {
			t.Errorf("bytes = %q, want %q", got, *want.BytesBase64)
		}
	}
	if want.requestIDPresent {
		wantID := ""
		if want.RequestID != nil {
			wantID = *want.RequestID
		}
		if result.RequestID != wantID {
			t.Errorf("requestId = %q, want %q", result.RequestID, wantID)
		}
	}
}

func runRetryCase(t *testing.T, g given, e expectation) {
	t.Helper()

	call := g
	call.Source, call.Target, call.BodyText = "zpl", "zpl", "^XA^XZ"

	result, err, transport, sleeper, _ := execute(t, call, g.Responses, nil)

	if e.Error != nil {
		assertError(t, *e.Error, err)
	} else {
		if err != nil {
			t.Fatalf("unexpected error: %v", err)
		}
		if e.Result != nil && e.Result.Text != nil && result.Text() != *e.Result.Text {
			t.Errorf("text = %q, want %q", result.Text(), *e.Result.Text)
		}
	}

	if e.Attempts != nil && transport.count() != *e.Attempts {
		t.Errorf("attempts = %d, want %d", transport.count(), *e.Attempts)
	}
	if got := sleeper.seconds(); !equalSeconds(got, e.SleepsSeconds) {
		t.Errorf("sleepsSeconds = %v, want %v", got, e.SleepsSeconds)
	}
}

func runValidationCase(t *testing.T, g given, e expectation) {
	t.Helper()

	_, err, transport, _, _ := execute(t, g, []scriptedResponse{okResponse}, nil)

	var validation *labelzoom.ValidationError
	if !errors.As(err, &validation) {
		t.Fatalf("expected a *labelzoom.ValidationError, got %#v", err)
	}
	if validation.Parameter != e.ValidationError.Parameter {
		t.Errorf("parameter = %q, want %q", validation.Parameter, e.ValidationError.Parameter)
	}

	// A local rejection is not an API error, so code catching *APIError to implement a
	// fallback must not swallow it.
	var api *labelzoom.APIError
	if errors.As(err, &api) {
		t.Error("a ValidationError must not unwrap to *APIError")
	}

	// Local validation must never reach the network.
	if e.RequestsSent != nil && transport.count() != *e.RequestsSent {
		t.Errorf("requests sent = %d, want %d", transport.count(), *e.RequestsSent)
	}
}

// ---------------------------------------------------------------------- assertions

// errorKinds maps spec.json's symbolic kind names onto this SDK's types. The Go column of
// docs/CONFORMANCE.md section 5 names these.
var errorKinds = map[string]func(error) bool{
	"BadRequest":      func(err error) bool { var e *labelzoom.BadRequestError; return errors.As(err, &e) },
	"Unauthorized":    func(err error) bool { var e *labelzoom.UnauthorizedError; return errors.As(err, &e) },
	"Forbidden":       func(err error) bool { var e *labelzoom.ForbiddenError; return errors.As(err, &e) },
	"NotFound":        func(err error) bool { var e *labelzoom.NotFoundError; return errors.As(err, &e) },
	"PayloadTooLarge": func(err error) bool { var e *labelzoom.PayloadTooLargeError; return errors.As(err, &e) },
	"RateLimited":     func(err error) bool { var e *labelzoom.RateLimitedError; return errors.As(err, &e) },
	"ServerError":     func(err error) bool { var e *labelzoom.ServerError; return errors.As(err, &e) },
}

func assertError(t *testing.T, want expectedError, err error) {
	t.Helper()

	var api *labelzoom.APIError
	if !errors.As(err, &api) {
		t.Fatalf("expected an API error, got %#v", err)
	}

	if want.Kind != nil {
		matches, known := errorKinds[*want.Kind]
		if !known {
			t.Fatalf("fixture names error kind %q, which the Go runner does not map", *want.Kind)
		}
		if !matches(err) {
			t.Errorf("error is %T, want kind %s", err, *want.Kind)
		}
	}
	if want.Status != nil && api.Status != *want.Status {
		t.Errorf("status = %d, want %d", api.Status, *want.Status)
	}
	if want.Message != nil && api.Message != *want.Message {
		t.Errorf("message = %q, want %q", api.Message, *want.Message)
	}
	if want.MessageNonEmpty && strings.TrimSpace(api.Message) == "" {
		t.Error("message must not be empty")
	}
	if want.MessageMaxLength != nil && len([]rune(api.Message)) > *want.MessageMaxLength {
		t.Errorf("message is %d characters, want at most %d",
			len([]rune(api.Message)), *want.MessageMaxLength)
	}
	if want.RawBodyLength != nil && len(api.RawBody) != *want.RawBodyLength {
		t.Errorf("rawBody is %d bytes, want %d", len(api.RawBody), *want.RawBodyLength)
	}
	if want.RawBodyPresent && api.RawBody == "" {
		t.Error("rawBody must be present")
	}
	if want.RequestID != nil && api.RequestID != *want.RequestID {
		t.Errorf("requestId = %q, want %q", api.RequestID, *want.RequestID)
	}
	if want.IsPaidFeature != nil {
		var forbidden *labelzoom.ForbiddenError
		if !errors.As(err, &forbidden) {
			t.Fatalf("isPaidFeature asserted on %T, which is not a *ForbiddenError", err)
		}
		if forbidden.IsPaidFeature != *want.IsPaidFeature {
			t.Errorf("isPaidFeature = %v, want %v", forbidden.IsPaidFeature, *want.IsPaidFeature)
		}
	}
	if want.RetryAfterSeconds != nil {
		var limited *labelzoom.RateLimitedError
		if !errors.As(err, &limited) {
			t.Fatalf("retryAfterSeconds asserted on %T, which is not a *RateLimitedError", err)
		}
		if limited.RetryAfterSeconds == nil {
			t.Fatalf("retryAfterSeconds is absent, want %v", *want.RetryAfterSeconds)
		}
		if *limited.RetryAfterSeconds != *want.RetryAfterSeconds {
			t.Errorf("retryAfterSeconds = %v, want %v",
				*limited.RetryAfterSeconds, *want.RetryAfterSeconds)
		}
	}
}

func equalSeconds(got, want []float64) bool {
	if len(got) != len(want) {
		return false
	}
	for i := range got {
		if math.Abs(got[i]-want[i]) > 1e-6 {
			return false
		}
	}
	return true
}

func contains(haystack []string, needle string) bool {
	for _, value := range haystack {
		if value == needle {
			return true
		}
	}
	return false
}

func missing(want, got []string) []string {
	var absent []string
	for _, id := range want {
		if !contains(got, id) {
			absent = append(absent, id)
		}
	}
	return absent
}
