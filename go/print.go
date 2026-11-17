package labelzoom

import (
	"bytes"
	"context"
	"crypto/rand"
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"net/url"
	"strconv"
	"strings"
	"time"
)

// jsonMediaType is the Accept of every print call, and the Content-Type of a template print.
const jsonMediaType = "application/json"

// The copies range the print endpoints accept.
const (
	minCopies = 1
	maxCopies = 9999
)

// PrintRequest describes one document print.
//
// A print has no target format: the printer's own configuration picks the output language,
// and LabelZoom converts the document to it, so a PDF prints on a ZPL printer. Printing needs
// an API key with the print scope.
type PrintRequest struct {
	// PrinterID is the Cloud Print printer to send the document to.
	PrinterID string
	// Source is the format of Body. Only the formats in [PrintSourceFormats] can be printed;
	// convert anything else to the printer's language first, then print the result.
	Source SourceFormat
	// Body is the document, as the format's own bytes.
	Body []byte
	// Options are the source options: how the document is read. Nil sends none, and the
	// document goes to the printer as is when it is already in the printer's language.
	Options *PrintOptions
	// Copies sends the whole job this many times, 1 to 9999. It multiplies any quantity the
	// document sets itself: ^PQ3 with 3 copies prints 9 labels. Nil leaves it to the document.
	Copies *int
	// IdempotencyKey makes the print safe to repeat: the same key returns the original job
	// instead of printing again. Use something unique to the label, such as an order number.
	// Empty has the SDK generate a key per call, which its own retries reuse.
	IdempotencyKey string
}

// TemplatePrintRequest describes one template print: a template published from LabelZoom
// Studio or the web designer, filled with one record per label.
type TemplatePrintRequest struct {
	// PrinterID is the Cloud Print printer to send the labels to.
	PrinterID string
	// TemplateID is the published template.
	TemplateID string
	// Data holds the merge-field values: a [DataRecord] for one label, a []DataRecord for
	// several, or any JSON-shaped equivalent. Nil prints a template with no merge fields.
	Data any
	// Validate checks every record for missing merge fields before anything prints.
	Validate bool
	// Copies prints each label this many times, 1 to 9999. Nil prints one of each.
	Copies *int
	// IdempotencyKey is one key for the whole batch; the server derives each record's key
	// from it. Empty has the SDK generate one per call.
	IdempotencyKey string
}

// PrintOptions are the source options of a print: how the document is read.
//
// They are the source half of [Options], with the same meaning, the same validation and the
// same wire shape. There are no target options, because the printer picks the output.
type PrintOptions struct {
	// SourceDPI is the resolution the document was made for. Server default 203. Not for
	// PDF or LabelZoom XML/JSON sources, which have no dots.
	SourceDPI *int
	// Scaling is a percentage, for an image or PDF source. Server default 100.
	Scaling *float64
	// ColorMode selects color reduction for an image or PDF source. Server default GRAYSCALE.
	ColorMode *ColorMode
	// Darkness is a luminance threshold from 0 to 100, for an image or PDF source. Server
	// default 70.
	Darkness *int
	// Position places an image or PDF source on the label. A negative offset crops it.
	Position *Position
	// Data holds the variable-field values, one record per label. See [Options].Data.
	Data any
	// Label is the media size in INCHES. Omit it to use the size the document declares.
	Label *LabelSize
	// PDF configures how a PDF source is read.
	PDF *PDFOptions
	// ZPL configures how a ZPL source is read.
	ZPL *ZPLSourceOptions
	// Split cuts each page of an image or PDF source into a grid of labels.
	Split *SplitOptions
	// LabelIndex is the ZERO-BASED index of the one label to print.
	LabelIndex *int
	// Extra carries source options this SDK does not model yet. Its keys are merged at the
	// top level of the params object and override the fields above.
	Extra map[string]any
}

// ZPLSourceOptions configures how a ZPL source is read. It is the source half of
// [ZPLOptions].
type ZPLSourceOptions struct {
	// CommandsToIgnore skips the named commands while reading, e.g. []string{"^PQ"}.
	CommandsToIgnore []string
}

// options maps the print options onto [Options], so they go through the one serializer the
// convert path uses and cannot disagree with it on validation or on the JSON shape.
func (o *PrintOptions) options() *Options {
	if o == nil {
		return nil
	}
	options := &Options{
		SourceDPI:  o.SourceDPI,
		Scaling:    o.Scaling,
		ColorMode:  o.ColorMode,
		Darkness:   o.Darkness,
		Position:   o.Position,
		Data:       o.Data,
		Label:      o.Label,
		PDF:        o.PDF,
		Split:      o.Split,
		LabelIndex: o.LabelIndex,
		Extra:      o.Extra,
	}
	if o.ZPL != nil {
		options.ZPL = &ZPLOptions{CommandsToIgnore: o.ZPL.CommandsToIgnore}
	}
	return options
}

// PrintJob is a print job the server accepted.
type PrintJob struct {
	// JobID identifies the job. Pass it to [Client.GetPrintJob] to follow it.
	JobID string
	// Status is "dispatched" when an online agent took the job and "queued" when none is
	// online. It stays a string because the set of statuses can grow.
	Status string
	// Copies is the copies requested, or nil when the request set none.
	Copies *int
	// Idempotent is true when the request reused an Idempotency-Key: this is the original
	// job, and nothing printed again.
	Idempotent bool
}

// PrintBatch is the jobs a template print created, one per record.
type PrintBatch struct {
	Jobs []PrintJob
	// Remaining is the number of records at the end of the batch that weren't submitted.
	// Send the same request with the same IdempotencyKey to submit them; the records already
	// submitted replay without printing again.
	Remaining int
	// Message is the server's explanation when Remaining isn't 0, else "".
	Message string
}

// PrintJobDetails is a print job's current state.
type PrintJobDetails struct {
	ID        string
	PrinterID string
	AgentID   string
	// SourceFormat is the format the job was stored in: the printer's language after any
	// conversion.
	SourceFormat string
	// Copies is the copies requested, or nil when the request set none.
	Copies *int
	// Status is "queued", "dispatched", "printing", "completed" or "failed". It stays a string
	// because the set of statuses can grow.
	Status string
	// Error is why the job failed, or "".
	Error      string
	RetryCount int
	// The timestamps are nil until the job reaches that point.
	CreatedDate    *time.Time
	DispatchedDate *time.Time
	CompletedDate  *time.Time
	RetriedDate    *time.Time
}

// PrintQuota is the account's Cloud Print limits and today's usage.
type PrintQuota struct {
	Plan     string
	Agents   PrintQuotaCounter
	Printers PrintQuotaCounter
	// JobsToday resets at JobsToday.ResetAt, 00:00 UTC.
	JobsToday PrintQuotaCounter
	Limits    PrintQuotaLimits
}

// PrintQuotaCounter is one counted quota.
type PrintQuotaCounter struct {
	Used      int
	Limit     int
	Remaining int
	// Pending counts agents waiting to be approved. Nil for anything but agents.
	Pending *int
	// ResetAt is when the counter resets. Nil for anything but jobs today.
	ResetAt *time.Time
}

// PrintQuotaLimits are the size limits of a print request.
type PrintQuotaLimits struct {
	MaxPayloadBytes       int64
	MaxStoredPayloadBytes int64
	// MaxBatchSize is the most records one template print takes.
	MaxBatchSize int
}

// printableSources are the formats the print endpoint converts to the printer's language
// (contract P2). It sends any other format to the printer as is, so the SDK refuses them.
var printableSources = map[SourceFormat]bool{
	SourceZPL: true, SourceEPL: true, SourceIPL: true, SourceTSPL: true, SourceDPL: true,
	SourceSBPL: true, SourceFingerprint: true, SourceCPCL: true, SourceXML: true,
	SourceJSON: true, SourcePDF: true, SourcePNG: true, SourceBMP: true, SourceGIF: true,
	SourceJPEG: true, SourceJPG: true,
}

// PrintSourceFormats lists the sources [Client.Print] accepts, in the contract's order.
func PrintSourceFormats() []SourceFormat {
	var formats []SourceFormat
	for _, format := range SourceFormats() {
		if printableSources[format] {
			formats = append(formats, format)
		}
	}
	return formats
}

// Print sends a document to a Cloud Print printer.
//
// It returns the same typed errors as [Client.Convert], plus [UnprocessableError] and
// [PrintQuotaExceededError]. A request rejected before it leaves the process returns a
// [ValidationError].
func (c *Client) Print(ctx context.Context, request PrintRequest) (*PrintJob, error) {
	printerID, err := requireID(request.PrinterID, "printerId")
	if err != nil {
		return nil, err
	}
	if !printableSources[request.Source] {
		return nil, &ValidationError{
			Parameter: "source",
			Message: fmt.Sprintf("%q can't be printed directly: the print endpoint converts only ZPL, "+
				"EPL, IPL, TSPL, DPL, SBPL, Fingerprint, CPCL, LabelZoom XML and JSON, PDF and images. "+
				"Convert it to the printer's language with Convert first, then print the result.",
				string(request.Source)),
		}
	}
	if len(request.Body) == 0 {
		return nil, &ValidationError{
			Parameter: "body",
			Message:   "The document cannot be empty; the API rejects an empty print request.",
		}
	}
	mediaType, err := request.Source.mediaType()
	if err != nil {
		return nil, err
	}

	query := url.Values{}
	// Always sent: the server can't tell ZPL from EPL by Content-Type, and without it the
	// document goes to the printer unconverted.
	query.Set("sourceFormat", request.Source.wireToken())
	if err := setCopies(query, request.Copies); err != nil {
		return nil, err
	}
	// An empty params would make the server convert a document already in the printer's
	// language, so it is sent only when a source option was set (contract P2).
	params, err := request.Options.options().serialize()
	if err != nil {
		return nil, err
	}
	if params != "" {
		query.Set("params", params)
	}
	key, err := keyForCall(request.IdempotencyKey)
	if err != nil {
		return nil, err
	}

	endpoint := fmt.Sprintf("%s/api/v3/printers/%s/print?%s",
		c.baseURL, url.PathEscape(printerID), query.Encode())

	var job wirePrintJob
	if err := c.sendJSON(ctx, call{
		method:         http.MethodPost,
		endpoint:       endpoint,
		contentType:    mediaType,
		idempotencyKey: key,
		body:           request.Body,
	}, &job); err != nil {
		return nil, err
	}
	result := job.printJob()
	return &result, nil
}

// PrintTemplate prints a published template, one label per record.
//
// A template print always returns a [PrintBatch], whatever the number of records.
func (c *Client) PrintTemplate(ctx context.Context, request TemplatePrintRequest) (*PrintBatch, error) {
	printerID, err := requireID(request.PrinterID, "printerId")
	if err != nil {
		return nil, err
	}
	templateID, err := requireID(request.TemplateID, "templateId")
	if err != nil {
		return nil, err
	}

	var records []any
	if request.Data != nil {
		if records, err = normalizeData(request.Data); err != nil {
			return nil, err
		}
	}

	query := url.Values{}
	if err := setCopies(query, request.Copies); err != nil {
		return nil, err
	}
	if request.Validate {
		query.Set("validate", "true")
	}
	key, err := keyForCall(request.IdempotencyKey)
	if err != nil {
		return nil, err
	}

	// One record is sent as that object, several as an array, and none as {} (contract P3).
	var payload any = map[string]any{}
	switch len(records) {
	case 0:
	case 1:
		payload = records[0]
	default:
		payload = records
	}
	body, err := json.Marshal(payload)
	if err != nil {
		return nil, fmt.Errorf("labelzoom: could not serialize the template data: %w", err)
	}

	endpoint := fmt.Sprintf("%s/api/v3/printers/%s/templates/%s/print",
		c.baseURL, url.PathEscape(printerID), url.PathEscape(templateID))
	if len(query) > 0 {
		endpoint += "?" + query.Encode()
	}

	var batch wirePrintBatch
	if err := c.sendJSON(ctx, call{
		method:         http.MethodPost,
		endpoint:       endpoint,
		contentType:    jsonMediaType,
		idempotencyKey: key,
		body:           body,
	}, &batch); err != nil {
		return nil, err
	}
	return batch.printBatch(), nil
}

// GetPrintJob reads a print job's current state, to follow it to completion.
func (c *Client) GetPrintJob(ctx context.Context, jobID string) (*PrintJobDetails, error) {
	id, err := requireID(jobID, "jobId")
	if err != nil {
		return nil, err
	}

	var details wirePrintJobDetails
	if err := c.sendJSON(ctx, call{
		method:   http.MethodGet,
		endpoint: fmt.Sprintf("%s/api/v3/jobs/%s", c.baseURL, url.PathEscape(id)),
	}, &details); err != nil {
		return nil, err
	}
	result := details.printJobDetails()
	return &result, nil
}

// GetPrintQuota reads the account's Cloud Print limits and today's usage, so you can stop
// before the daily limit's [PrintQuotaExceededError].
func (c *Client) GetPrintQuota(ctx context.Context) (*PrintQuota, error) {
	var quota wirePrintQuota
	if err := c.sendJSON(ctx, call{
		method:   http.MethodGet,
		endpoint: c.baseURL + "/api/v3/print/quota",
	}, &quota); err != nil {
		return nil, err
	}
	result := quota.printQuota()
	return &result, nil
}

// sendJSON sends a print call under the retry policy and decodes its JSON response into into.
// Every print response is JSON, so every print call asks for it (contract P1).
func (c *Client) sendJSON(ctx context.Context, request call, into any) error {
	request.accept = jsonMediaType
	return c.send(ctx, request, func(response *http.Response) error {
		defer func() { _ = response.Body.Close() }()
		body, err := io.ReadAll(response.Body)
		if err != nil {
			return fmt.Errorf("labelzoom: could not read the print response: %w", err)
		}
		if err := json.Unmarshal(body, into); err != nil {
			return fmt.Errorf("labelzoom: the print response is not the JSON expected: %w", err)
		}
		return nil
	})
}

// ------------------------------------------------------------------- validation

func requireID(id, parameter string) (string, error) {
	if strings.TrimSpace(id) == "" {
		return "", &ValidationError{Parameter: parameter, Message: parameter + " cannot be empty."}
	}
	return id, nil
}

// setCopies validates copies and adds it to the query when set. Both print calls use it, so
// they can't validate it differently.
func setCopies(query url.Values, copies *int) error {
	if copies == nil {
		return nil
	}
	if *copies < minCopies || *copies > maxCopies {
		return &ValidationError{
			Parameter: "copies",
			Message: fmt.Sprintf("copies must be a whole number from %d to %d, but was %d.",
				minCopies, maxCopies, *copies),
		}
	}
	query.Set("copies", strconv.Itoa(*copies))
	return nil
}

// keyForCall is the Idempotency-Key for one call: the caller's, or a random UUID v4. It is
// fixed before the first attempt, so every retry of the call sends the same one (contract P4)
// and a retry after a lost response can't print twice.
func keyForCall(callerKey string) (string, error) {
	if callerKey != "" {
		if strings.TrimSpace(callerKey) == "" {
			return "", &ValidationError{
				Parameter: "idempotencyKey",
				Message:   "An Idempotency-Key cannot be blank. Leave it empty to have the SDK generate one.",
			}
		}
		return callerKey, nil
	}
	return newUUID()
}

// newUUID returns a random, lowercase RFC 9562 version 4 UUID.
func newUUID() (string, error) {
	var b [16]byte
	if _, err := rand.Read(b[:]); err != nil {
		return "", fmt.Errorf("labelzoom: could not generate an Idempotency-Key: %w", err)
	}
	b[6] = b[6]&0x0f | 0x40 // version 4
	b[8] = b[8]&0x3f | 0x80 // RFC 9562 variant
	return fmt.Sprintf("%x-%x-%x-%x-%x", b[0:4], b[4:6], b[6:8], b[8:10], b[10:16]), nil
}

// -------------------------------------------------------------------- responses

// The wire shapes. Members the SDK doesn't know are ignored, and status stays a string, so a
// status the server adds later can't fail the call (contract P6).

type wirePrintJob struct {
	JobID      string `json:"jobId"`
	Status     string `json:"status"`
	Copies     *int   `json:"copies"`
	Idempotent bool   `json:"idempotent"`
}

func (w wirePrintJob) printJob() PrintJob {
	return PrintJob{JobID: w.JobID, Status: w.Status, Copies: w.Copies, Idempotent: w.Idempotent}
}

// wirePrintBatch reads both shapes a template print returns: several records come back as
// {"jobs": [...]}, one as a bare job. Callers get one shape either way.
type wirePrintBatch struct {
	batch PrintBatch
}

func (w *wirePrintBatch) UnmarshalJSON(data []byte) error {
	var members map[string]json.RawMessage
	if err := json.Unmarshal(data, &members); err != nil {
		return err
	}
	if jobs, ok := members["jobs"]; ok && bytes.HasPrefix(bytes.TrimSpace(jobs), []byte("[")) {
		var batch struct {
			Jobs      []wirePrintJob `json:"jobs"`
			Remaining int            `json:"remaining"`
			Message   *string        `json:"message"`
		}
		if err := json.Unmarshal(data, &batch); err != nil {
			return err
		}
		w.batch.Jobs = make([]PrintJob, 0, len(batch.Jobs))
		for _, job := range batch.Jobs {
			w.batch.Jobs = append(w.batch.Jobs, job.printJob())
		}
		w.batch.Remaining = batch.Remaining
		if batch.Message != nil {
			w.batch.Message = *batch.Message
		}
		return nil
	}

	var job wirePrintJob
	if err := json.Unmarshal(data, &job); err != nil {
		return err
	}
	w.batch = PrintBatch{Jobs: []PrintJob{job.printJob()}}
	return nil
}

func (w *wirePrintBatch) printBatch() *PrintBatch { return &w.batch }

// wirePrintJobDetails is snake_case on the wire.
type wirePrintJobDetails struct {
	ID             string  `json:"id"`
	PrinterID      *string `json:"printer_id"`
	AgentID        *string `json:"agent_id"`
	SourceFormat   *string `json:"source_format"`
	Copies         *int    `json:"copies"`
	Status         string  `json:"status"`
	Error          *string `json:"error"`
	RetryCount     int     `json:"retry_count"`
	CreatedDate    *string `json:"created_date"`
	DispatchedDate *string `json:"dispatched_date"`
	CompletedDate  *string `json:"completed_date"`
	RetriedDate    *string `json:"retried_date"`
}

func (w wirePrintJobDetails) printJobDetails() PrintJobDetails {
	return PrintJobDetails{
		ID:             w.ID,
		PrinterID:      deref(w.PrinterID),
		AgentID:        deref(w.AgentID),
		SourceFormat:   deref(w.SourceFormat),
		Copies:         w.Copies,
		Status:         w.Status,
		Error:          deref(w.Error),
		RetryCount:     w.RetryCount,
		CreatedDate:    instant(w.CreatedDate),
		DispatchedDate: instant(w.DispatchedDate),
		CompletedDate:  instant(w.CompletedDate),
		RetriedDate:    instant(w.RetriedDate),
	}
}

type wireQuotaCounter struct {
	Used      int     `json:"used"`
	Limit     int     `json:"limit"`
	Remaining int     `json:"remaining"`
	Pending   *int    `json:"pending"`
	ResetAt   *string `json:"resetAt"`
}

func (w wireQuotaCounter) counter() PrintQuotaCounter {
	return PrintQuotaCounter{
		Used:      w.Used,
		Limit:     w.Limit,
		Remaining: w.Remaining,
		Pending:   w.Pending,
		ResetAt:   instant(w.ResetAt),
	}
}

type wirePrintQuota struct {
	Plan      string           `json:"plan"`
	Agents    wireQuotaCounter `json:"agents"`
	Printers  wireQuotaCounter `json:"printers"`
	JobsToday wireQuotaCounter `json:"jobsToday"`
	Limits    struct {
		MaxPayloadBytes       int64 `json:"maxPayloadBytes"`
		MaxStoredPayloadBytes int64 `json:"maxStoredPayloadBytes"`
		MaxBatchSize          int   `json:"maxBatchSize"`
	} `json:"limits"`
}

func (w wirePrintQuota) printQuota() PrintQuota {
	return PrintQuota{
		Plan:      w.Plan,
		Agents:    w.Agents.counter(),
		Printers:  w.Printers.counter(),
		JobsToday: w.JobsToday.counter(),
		Limits: PrintQuotaLimits{
			MaxPayloadBytes:       w.Limits.MaxPayloadBytes,
			MaxStoredPayloadBytes: w.Limits.MaxStoredPayloadBytes,
			MaxBatchSize:          w.Limits.MaxBatchSize,
		},
	}
}

func deref(value *string) string {
	if value == nil {
		return ""
	}
	return *value
}

// instant parses an RFC 3339 timestamp. Null, absent and unparseable all read as nil: a
// timestamp the SDK can't read must not fail a call whose job did go through.
func instant(value *string) *time.Time {
	if value == nil {
		return nil
	}
	parsed, err := time.Parse(time.RFC3339Nano, *value)
	if err != nil {
		return nil
	}
	return &parsed
}
