package labelzoom_test

import (
	"context"
	"encoding/json"
	"errors"
	"reflect"
	"strconv"
	"testing"
	"time"

	labelzoom "github.com/labelzoom/labelzoom-sdk/go"
)

// runPrintCase executes one print/* fixture: the "print" kind of docs/CONFORMANCE.md.
func runPrintCase(t *testing.T, g given, e expectation) {
	t.Helper()

	// given.maxRetries defaults to 0 for print cases.
	client, transport, sleeper, _ := newClient(t, g, g.Responses, labelzoom.Ptr(0))
	result, err := runPrintOperation(t, client, g)

	if e.ValidationError != nil {
		var validation *labelzoom.ValidationError
		if !errors.As(err, &validation) {
			t.Fatalf("expected a *labelzoom.ValidationError, got %#v", err)
		}
		if validation.Parameter != e.ValidationError.Parameter {
			t.Errorf("parameter = %q, want %q", validation.Parameter, e.ValidationError.Parameter)
		}
		var api *labelzoom.APIError
		if errors.As(err, &api) {
			t.Error("a ValidationError must not unwrap to *APIError")
		}
		if e.RequestsSent != nil && transport.count() != *e.RequestsSent {
			t.Errorf("requests sent = %d, want %d", transport.count(), *e.RequestsSent)
		}
		return
	}

	if e.Error != nil {
		assertError(t, *e.Error, err)
	} else {
		if err != nil {
			t.Fatalf("unexpected error: %v", err)
		}
		if e.Result == nil {
			t.Fatal("the fixture gives neither a result nor an error")
		}
		assertPrintResult(t, *e.Result, result)
	}

	if e.Request != nil {
		request, body := transport.first()
		if request == nil {
			t.Fatal("no request was sent")
		}
		assertRequest(t, *e.Request, request, body)
	}
	if e.Attempts != nil && transport.count() != *e.Attempts {
		t.Errorf("attempts = %d, want %d", transport.count(), *e.Attempts)
	}
	if e.SleepsSeconds != nil {
		if got := sleeper.seconds(); !equalSeconds(got, e.SleepsSeconds) {
			t.Errorf("sleepsSeconds = %v, want %v", got, e.SleepsSeconds)
		}
	}
	for _, name := range e.SameHeaderAcrossAttempts {
		requests := transport.all()
		first := requests[0].Header.Get(name)
		if first == "" {
			t.Errorf("header %s is missing on the first attempt", name)
		}
		for index, request := range requests[1:] {
			if got := request.Header.Get(name); got != first {
				t.Errorf("header %s on attempt %d = %q, want %q as on the first", name, index+2, got, first)
			}
		}
	}
}

// runPrintOperation makes the call the fixture names, through the public surface.
func runPrintOperation(t *testing.T, client *labelzoom.Client, g given) (any, error) {
	t.Helper()
	ctx := context.Background()

	key := ""
	if g.IdempotencyKey != nil {
		key = *g.IdempotencyKey
	}

	switch g.Operation {
	case "print":
		return client.Print(ctx, labelzoom.PrintRequest{
			PrinterID:      g.PrinterID,
			Source:         labelzoom.SourceFormat(g.Source),
			Body:           []byte(g.BodyText),
			Options:        buildPrintOptions(t, g.Options),
			Copies:         g.Copies,
			IdempotencyKey: key,
		})
	case "printTemplate":
		var data any
		if g.Data != nil {
			if err := json.Unmarshal(g.Data, &data); err != nil {
				t.Fatalf("could not parse given.data: %v", err)
			}
		}
		validate := g.Validate != nil && *g.Validate
		return client.PrintTemplate(ctx, labelzoom.TemplatePrintRequest{
			PrinterID:      g.PrinterID,
			TemplateID:     g.TemplateID,
			Data:           data,
			Validate:       validate,
			Copies:         g.Copies,
			IdempotencyKey: key,
		})
	case "getJob":
		return client.GetPrintJob(ctx, g.JobID)
	case "getQuota":
		return client.GetPrintQuota(ctx)
	default:
		t.Fatalf("unknown print operation %q", g.Operation)
		return nil, nil
	}
}

// buildPrintOptions reuses the convert translation, then moves the source options across. A
// fixture setting a target option on a print is a fixture bug, so it fails loudly.
func buildPrintOptions(t *testing.T, wire map[string]any) *labelzoom.PrintOptions {
	t.Helper()
	options := buildOptions(t, wire)
	if options == nil {
		return nil
	}
	if options.TargetDPI != nil || options.Rotation != nil || options.Watermark != nil ||
		options.Dialect != nil || options.Layout != nil || options.RawQuery != nil ||
		(options.ZPL != nil && (options.ZPL.ImageCompression != nil || options.ZPL.CollapseCopies != nil)) {
		t.Fatalf("print fixture sets a target option, which a print does not take: %v", wire)
	}

	printOptions := &labelzoom.PrintOptions{
		SourceDPI:  options.SourceDPI,
		Scaling:    options.Scaling,
		ColorMode:  options.ColorMode,
		Darkness:   options.Darkness,
		Position:   options.Position,
		Data:       options.Data,
		Label:      options.Label,
		PDF:        options.PDF,
		Split:      options.Split,
		LabelIndex: options.LabelIndex,
	}
	if options.ZPL != nil {
		printOptions.ZPL = &labelzoom.ZPLSourceOptions{CommandsToIgnore: options.ZPL.CommandsToIgnore}
	}
	return printOptions
}

// assertPrintResult compares the one result member the fixture gives against the SDK's
// result, as a subset.
func assertPrintResult(t *testing.T, want expectedResult, got any) {
	t.Helper()

	var name string
	var raw json.RawMessage
	var actual any
	switch {
	case want.PrintJob != nil:
		name, raw = "printJob", want.PrintJob
		job, ok := got.(*labelzoom.PrintJob)
		if !ok {
			t.Fatalf("result is %T, want *labelzoom.PrintJob", got)
		}
		actual = projectJob(*job)
	case want.PrintBatch != nil:
		name, raw = "printBatch", want.PrintBatch
		batch, ok := got.(*labelzoom.PrintBatch)
		if !ok {
			t.Fatalf("result is %T, want *labelzoom.PrintBatch", got)
		}
		jobs := []any{}
		for _, job := range batch.Jobs {
			jobs = append(jobs, projectJob(job))
		}
		actual = map[string]any{
			"jobs": jobs, "remaining": float64(batch.Remaining), "message": nullable(batch.Message),
		}
	case want.Job != nil:
		name, raw = "job", want.Job
		job, ok := got.(*labelzoom.PrintJobDetails)
		if !ok {
			t.Fatalf("result is %T, want *labelzoom.PrintJobDetails", got)
		}
		actual = map[string]any{
			"id":             job.ID,
			"printerId":      nullable(job.PrinterID),
			"agentId":        nullable(job.AgentID),
			"sourceFormat":   nullable(job.SourceFormat),
			"copies":         nullableInt(job.Copies),
			"status":         job.Status,
			"error":          nullable(job.Error),
			"retryCount":     float64(job.RetryCount),
			"createdDate":    job.CreatedDate,
			"dispatchedDate": job.DispatchedDate,
			"completedDate":  job.CompletedDate,
			"retriedDate":    job.RetriedDate,
		}
	case want.Quota != nil:
		name, raw = "quota", want.Quota
		quota, ok := got.(*labelzoom.PrintQuota)
		if !ok {
			t.Fatalf("result is %T, want *labelzoom.PrintQuota", got)
		}
		counter := func(c labelzoom.PrintQuotaCounter) map[string]any {
			out := map[string]any{
				"used": float64(c.Used), "limit": float64(c.Limit), "remaining": float64(c.Remaining),
			}
			if c.Pending != nil {
				out["pending"] = float64(*c.Pending)
			}
			if c.ResetAt != nil {
				out["resetAt"] = c.ResetAt
			}
			return out
		}
		actual = map[string]any{
			"plan":      quota.Plan,
			"agents":    counter(quota.Agents),
			"printers":  counter(quota.Printers),
			"jobsToday": counter(quota.JobsToday),
			"limits": map[string]any{
				"maxPayloadBytes":       float64(quota.Limits.MaxPayloadBytes),
				"maxStoredPayloadBytes": float64(quota.Limits.MaxStoredPayloadBytes),
				"maxBatchSize":          float64(quota.Limits.MaxBatchSize),
			},
		}
	default:
		t.Fatal("expect.result gives none of printJob, printBatch, job or quota")
	}

	var expected any
	if err := json.Unmarshal(raw, &expected); err != nil {
		t.Fatalf("could not parse expect.result.%s: %v", name, err)
	}
	assertSubset(t, name, expected, actual)
}

// projectJob puts a PrintJob in the fixtures' vocabulary.
func projectJob(job labelzoom.PrintJob) map[string]any {
	return map[string]any{
		"jobId":      job.JobID,
		"status":     job.Status,
		"copies":     nullableInt(job.Copies),
		"idempotent": job.Idempotent,
	}
}

// nullable maps Go's "no value", the empty string, onto the fixtures' null.
func nullable(value string) any {
	if value == "" {
		return nil
	}
	return value
}

func nullableInt(value *int) any {
	if value == nil {
		return nil
	}
	return float64(*value)
}

// assertSubset asserts every member the fixture gives, and only those. Timestamps compare as
// instants, so 2026-10-11T15:00:00.000Z matches 2026-10-11T15:00:00Z.
func assertSubset(t *testing.T, path string, want, got any) {
	t.Helper()

	if instant, ok := got.(*time.Time); ok {
		if want == nil {
			if instant != nil {
				t.Errorf("%s = %v, want null", path, instant)
			}
			return
		}
		text, isString := want.(string)
		if !isString {
			t.Errorf("%s is a timestamp, but the fixture gives %#v", path, want)
			return
		}
		expected, err := time.Parse(time.RFC3339Nano, text)
		if err != nil {
			t.Fatalf("%s: the fixture's %q is not an RFC 3339 timestamp: %v", path, text, err)
		}
		if instant == nil || !instant.Equal(expected) {
			t.Errorf("%s = %v, want %v", path, instant, expected)
		}
		return
	}

	switch expected := want.(type) {
	case map[string]any:
		actual, ok := got.(map[string]any)
		if !ok {
			t.Errorf("%s = %#v, want an object", path, got)
			return
		}
		for key, value := range expected {
			member, present := actual[key]
			if !present {
				t.Errorf("%s.%s is missing", path, key)
				continue
			}
			assertSubset(t, path+"."+key, value, member)
		}
	case []any:
		actual, ok := got.([]any)
		if !ok || len(actual) != len(expected) {
			t.Errorf("%s = %#v, want %d entries", path, got, len(expected))
			return
		}
		for index := range expected {
			assertSubset(t, path+"["+strconv.Itoa(index)+"]", expected[index], actual[index])
		}
	default:
		if !reflect.DeepEqual(got, want) {
			t.Errorf("%s = %#v, want %#v", path, got, want)
		}
	}
}
