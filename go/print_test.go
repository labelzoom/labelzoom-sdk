package labelzoom_test

import (
	"context"
	"encoding/json"
	"errors"
	"reflect"
	"regexp"
	"sort"
	"testing"
	"time"

	labelzoom "github.com/labelzoom/labelzoom-sdk/go"
)

var uuidV4 = regexp.MustCompile(`^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$`)

var acceptedJob = scriptedResponse{
	Status:   200,
	Headers:  map[string]string{"content-type": "application/json"},
	BodyText: `{"jobId":"job-1","status":"dispatched","copies":2}`,
}

func printClient(t *testing.T, script ...scriptedResponse) (*labelzoom.Client, *recordingTransport) {
	t.Helper()
	transport := newRecordingTransport(script)
	client, err := labelzoom.New(
		labelzoom.WithAPIKey("lz_test_print_key"),
		labelzoom.WithHTTPClient(transport.client()),
		labelzoom.WithSleeper(func(time.Duration) {}),
		labelzoom.WithoutJitter(),
		labelzoom.WithMaxRetries(0),
	)
	if err != nil {
		t.Fatalf("could not construct the client: %v", err)
	}
	return client, transport
}

// TestPrintTypedPath sends one document print through the public surface, with every source
// option set, and checks everything that goes out. The conformance suite runs a few options;
// this is what proves PrintOptions maps every one of its fields into params.
func TestPrintTypedPath(t *testing.T) {
	client, transport := printClient(t, acceptedJob)

	job, err := client.Print(context.Background(), labelzoom.PrintRequest{
		PrinterID: "printer/1",
		Source:    labelzoom.SourcePDF,
		Body:      []byte("%PDF-1.7"),
		Options: &labelzoom.PrintOptions{
			SourceDPI:  labelzoom.Ptr(300),
			Scaling:    labelzoom.Ptr(50.0),
			ColorMode:  labelzoom.Ptr(labelzoom.ColorModeBW),
			Darkness:   labelzoom.Ptr(60),
			Position:   &labelzoom.Position{X: 1, Y: 2},
			Data:       labelzoom.DataRecord{"sku": "A-100"},
			Label:      &labelzoom.LabelSize{Width: labelzoom.Ptr(4.0), Height: labelzoom.Ptr(6.0)},
			PDF:        &labelzoom.PDFOptions{PageNumber: labelzoom.Ptr(0)},
			ZPL:        &labelzoom.ZPLSourceOptions{CommandsToIgnore: []string{"^PQ"}},
			Split:      &labelzoom.SplitOptions{Rows: labelzoom.Ptr(2), Columns: labelzoom.Ptr(2)},
			LabelIndex: labelzoom.Ptr(1),
			Extra:      map[string]any{"future": true},
		},
		Copies:         labelzoom.Ptr(2),
		IdempotencyKey: "SO-1001",
	})
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if job.JobID != "job-1" || job.Status != "dispatched" || job.Copies == nil || *job.Copies != 2 || job.Idempotent {
		t.Errorf("job = %+v", job)
	}

	request, body := transport.last()
	// The id is one path segment, so its slash is escaped rather than read as a separator.
	if got := request.URL.EscapedPath(); got != "/api/v3/printers/printer%2F1/print" {
		t.Errorf("path = %q", got)
	}
	for name, want := range map[string]string{
		"Content-Type":    "application/pdf",
		"Accept":          "application/json",
		"Idempotency-Key": "SO-1001",
		"Authorization":   "Bearer lz_test_print_key",
	} {
		if got := request.Header.Get(name); got != want {
			t.Errorf("header %s = %q, want %q", name, got, want)
		}
	}
	if string(body) != "%PDF-1.7" {
		t.Errorf("body = %q", body)
	}

	query := request.URL.Query()
	if got := query.Get("sourceFormat"); got != "pdf" {
		t.Errorf("sourceFormat = %q", got)
	}
	if got := query.Get("copies"); got != "2" {
		t.Errorf("copies = %q", got)
	}
	var params map[string]any
	if err := json.Unmarshal([]byte(query.Get("params")), &params); err != nil {
		t.Fatalf("params is not JSON: %v", err)
	}
	want := map[string]any{
		"sourceDpi": 300.0, "scaling": 50.0, "colorMode": "BW", "darkness": 60.0,
		"position":   map[string]any{"x": 1.0, "y": 2.0},
		"data":       []any{map[string]any{"sku": "A-100"}},
		"label":      map[string]any{"width": 4.0, "height": 6.0},
		"pdf":        map[string]any{"pageNumber": 0.0},
		"zpl":        map[string]any{"commandsToIgnore": []any{"^PQ"}},
		"split":      map[string]any{"rows": 2.0, "columns": 2.0},
		"labelIndex": 1.0, "future": true,
	}
	if !reflect.DeepEqual(params, want) {
		t.Errorf("params = %#v\nwant     %#v", params, want)
	}
}

// TestPrintTemplateTypedPath sends a template print with typed records.
func TestPrintTemplateTypedPath(t *testing.T) {
	client, transport := printClient(t, scriptedResponse{
		Status:   202,
		Headers:  map[string]string{"content-type": "application/json"},
		BodyText: `{"jobs":[{"jobId":"a","status":"queued"},{"jobId":"b","status":"queued"}]}`,
	})

	batch, err := client.PrintTemplate(context.Background(), labelzoom.TemplatePrintRequest{
		PrinterID:  "printer-1",
		TemplateID: "template-1",
		Data:       []labelzoom.DataRecord{{"sku": "A-100"}, {"sku": "B-200"}},
		Validate:   true,
	})
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if len(batch.Jobs) != 2 || batch.Remaining != 0 || batch.Message != "" {
		t.Errorf("batch = %+v", batch)
	}

	request, body := transport.last()
	if request.URL.Path != "/api/v3/printers/printer-1/templates/template-1/print" {
		t.Errorf("path = %q", request.URL.Path)
	}
	if got := request.URL.RawQuery; got != "validate=true" {
		t.Errorf("query = %q", got)
	}
	if got := request.Header.Get("Content-Type"); got != "application/json" {
		t.Errorf("content type = %q", got)
	}
	if got := request.Header.Get("Idempotency-Key"); !uuidV4.MatchString(got) {
		t.Errorf("Idempotency-Key = %q, want a UUID v4", got)
	}
	var records any
	if err := json.Unmarshal(body, &records); err != nil {
		t.Fatalf("body is not JSON: %v", err)
	}
	if want := []any{map[string]any{"sku": "A-100"}, map[string]any{"sku": "B-200"}}; !reflect.DeepEqual(records, want) {
		t.Errorf("body = %#v", records)
	}
}

// TestPrintGeneratesAKeyPerCall pins the other half of P4: a key is reused across the
// retries of one call, but two calls never share one, or the second would replay the first.
func TestPrintGeneratesAKeyPerCall(t *testing.T) {
	client, transport := printClient(t, acceptedJob, acceptedJob)

	for range 2 {
		if _, err := client.Print(context.Background(), labelzoom.PrintRequest{
			PrinterID: "printer-1",
			Source:    labelzoom.SourceZPL,
			Body:      []byte("^XA^XZ"),
		}); err != nil {
			t.Fatalf("unexpected error: %v", err)
		}
	}

	requests := transport.all()
	first, second := requests[0].Header.Get("Idempotency-Key"), requests[1].Header.Get("Idempotency-Key")
	for _, key := range []string{first, second} {
		if !uuidV4.MatchString(key) {
			t.Errorf("Idempotency-Key = %q, want a UUID v4", key)
		}
	}
	if first == second {
		t.Errorf("two calls sent the same Idempotency-Key %q", first)
	}
}

// TestPrintOptionsAreTheSourceOptions is the Go counterpart of the chaining SDKs' stage-shape
// test. Every Options field is classified here, so adding one fails this test until someone
// decides whether print takes it; PrintOptions must then hold exactly the source half.
func TestPrintOptionsAreTheSourceOptions(t *testing.T) {
	source := []string{
		"SourceDPI", "Scaling", "ColorMode", "Darkness", "Position", "Data", "Label", "PDF", "ZPL",
		"Split", "LabelIndex", "Extra",
	}
	target := []string{"TargetDPI", "Rotation", "Watermark", "Dialect", "Layout", "RawQuery"}

	optionFields := fieldNames(reflect.TypeOf(labelzoom.Options{}))
	if classified := sortedUnion(source, target); !reflect.DeepEqual(optionFields, classified) {
		t.Errorf("Options fields %v are not all classified as source or target %v", optionFields, classified)
	}
	if printFields, want := fieldNames(reflect.TypeOf(labelzoom.PrintOptions{})), sortedUnion(source, nil); !reflect.DeepEqual(printFields, want) {
		t.Errorf("PrintOptions fields = %v, want exactly the source options %v", printFields, want)
	}

	// The shared fields have the same type, so they are written by the same serializer.
	optionsType, printType := reflect.TypeOf(labelzoom.Options{}), reflect.TypeOf(labelzoom.PrintOptions{})
	for _, name := range source {
		if name == "ZPL" {
			continue
		}
		convert, _ := optionsType.FieldByName(name)
		printField, _ := printType.FieldByName(name)
		if convert.Type != printField.Type {
			t.Errorf("PrintOptions.%s is %v, but Options.%s is %v", name, printField.Type, name, convert.Type)
		}
	}

	// ZPL's source half: the ZPL options a reader uses, and none of the writer's.
	if got, want := fieldNames(reflect.TypeOf(labelzoom.ZPLSourceOptions{})), []string{"CommandsToIgnore"}; !reflect.DeepEqual(got, want) {
		t.Errorf("ZPLSourceOptions fields = %v, want %v", got, want)
	}
}

// TestPrintQuotaExceededIsARateLimit pins the error design: the daily limit is caught by a
// *RateLimitedError handler and by an *APIError one, and carries the 429's fields.
func TestPrintQuotaExceededIsARateLimit(t *testing.T) {
	client, _ := printClient(t, scriptedResponse{
		Status:   429,
		Headers:  map[string]string{"content-type": "application/json", "retry-after": "3600"},
		BodyText: `{"message":"Daily print job limit reached.","code":"daily_job_limit_reached"}`,
	})

	_, err := client.Print(context.Background(), labelzoom.PrintRequest{
		PrinterID: "printer-1", Source: labelzoom.SourceZPL, Body: []byte("^XA^XZ"),
	})

	var quota *labelzoom.PrintQuotaExceededError
	var limited *labelzoom.RateLimitedError
	var api *labelzoom.APIError
	if !errors.As(err, &quota) || !errors.As(err, &limited) || !errors.As(err, &api) {
		t.Fatalf("err = %#v; want it to match *PrintQuotaExceededError, *RateLimitedError and *APIError", err)
	}
	if quota.Code != "daily_job_limit_reached" || api.Code != "daily_job_limit_reached" {
		t.Errorf("code = %q / %q", quota.Code, api.Code)
	}
	if quota.RetryAfterSeconds == nil || *quota.RetryAfterSeconds != 3600 {
		t.Errorf("retryAfterSeconds = %v", quota.RetryAfterSeconds)
	}
}

func fieldNames(structType reflect.Type) []string {
	var names []string
	for index := range structType.NumField() {
		names = append(names, structType.Field(index).Name)
	}
	sort.Strings(names)
	return names
}

func sortedUnion(a, b []string) []string {
	out := append(append([]string(nil), a...), b...)
	sort.Strings(out)
	return out
}
