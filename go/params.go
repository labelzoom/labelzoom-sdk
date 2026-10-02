package labelzoom

import (
	"encoding/json"
	"fmt"
)

// DataRecord holds one label's variable-field values, keyed by field name.
type DataRecord map[string]any

// Position is the pixel offset of the extracted region. Server default 0,0.
type Position struct {
	X int `json:"x"`
	Y int `json:"y"`
}

// LabelSize is the media size, in INCHES -- not dots, and not millimetres.
//
// Omitting it entirely is meaningful: it asks the server to detect the size. That is why
// the fields are pointers and why an unset LabelSize emits no "label" key at all.
type LabelSize struct {
	Width  *float64 `json:"width,omitempty"`
	Height *float64 `json:"height,omitempty"`
}

// PDFOptions configures how a PDF source is read.
type PDFOptions struct {
	// ConversionMode selects rasterizing versus native extraction. Server default IMAGE.
	ConversionMode *PDFConversionMode `json:"conversionMode,omitempty"`
	// PageNumber is ZERO-BASED. Omit it to convert every page.
	PageNumber *int `json:"pageNumber,omitempty"`
}

// ZPLOptions configures ZPL output.
type ZPLOptions struct {
	// CommandsToIgnore drops the named commands from the output, e.g. []string{"^PQ"}.
	CommandsToIgnore []string `json:"commandsToIgnore,omitempty"`
	// ImageCompression selects the encoding of embedded images. Server default Z64.
	ImageCompression *ZPLImageCompression `json:"imageCompression,omitempty"`
}

// Options are the conversion parameters, in the shape the API expects.
//
// Every field is optional and only the ones you set are sent. The SDK never fills in a
// client-side default, which is why the scalars are pointers -- see [Int], [Float64],
// [Bool] and [String]. Setting nothing at all produces a bare URL with no query string.
type Options struct {
	// SourceDPI is how the source's absolute positions are interpreted: dots for printer
	// languages, pixels for bitmap images, an override of the document's dpi for LabelZoom
	// XML/JSON. Not applicable to PDF sources (vector).
	SourceDPI *int
	// TargetDPI is the resolution the output is authored at. Applies to printer-language and
	// XML/JSON targets, and to raster targets when the source is a bitmap or PDF.
	// Server default 203.
	TargetDPI *int
	// DPI is the legacy alias for a single print resolution; the server applies it to whichever
	// side(s) of the chosen path support a dpi. Prefer SourceDPI/TargetDPI.
	//
	// Deprecated: use SourceDPI or TargetDPI.
	DPI *int
	// Rotation is degrees clockwise and must be a multiple of 90. Server default 0.
	Rotation *int
	// Scaling is a percentage. Server default 100.
	Scaling *float64
	// ColorMode selects colour reduction. Server default GRAYSCALE.
	ColorMode *ColorMode
	// Darkness is a luminance threshold from 0 to 100. Server default 70.
	Darkness *int
	// Position is the pixel offset of the extracted region.
	Position *Position
	// Watermark is forced on for the anonymous free tier regardless of what you set.
	Watermark *bool
	// Dialect selects a printer dialect, e.g. "moca". Requires a paid license.
	Dialect *string
	// Data holds the variable-field values, one record per output label.
	//
	// Accepts a [DataRecord], a []DataRecord, or any JSON-shaped equivalent. A single
	// record is wrapped into a one-element array rather than rejected, because one record
	// means one label. An element that is not an object is a local validation error.
	Data any
	// Label is the media size in INCHES. Omit it to have the server detect the size.
	Label *LabelSize
	// PDF configures how a PDF source is read.
	PDF *PDFOptions
	// ZPL configures ZPL output.
	ZPL *ZPLOptions
	// Extra carries anything this SDK does not model yet. Unknown keys are ignored
	// server-side, so it is a safe forward-compatibility escape hatch. Keys here are
	// merged at the top level of the params object and override the fields above.
	Extra map[string]any
	// RawQuery adds query parameters alongside params, for the rare case that a new API
	// feature is not carried inside params at all. Prefer Extra: everything the conversion
	// endpoint takes today travels in params.
	RawQuery map[string]string
}

// serialize renders the options as the params JSON value.
//
// Everything travels in one ?params=<JSON> parameter, never dot-notation. The API accepts
// both and merges them, but dot-notation cannot express every parameter -- ?data=[{}] is
// rejected with 400 while ?params={"data":[{}]} succeeds. One serialization path, no
// special cases.
//
// Returns "" when nothing was set, in which case no query parameter is emitted at all.
func (o *Options) serialize() (string, error) {
	if o == nil {
		return "", nil
	}

	params := map[string]any{}

	if o.SourceDPI != nil {
		params["sourceDpi"] = *o.SourceDPI
	}
	if o.TargetDPI != nil {
		params["targetDpi"] = *o.TargetDPI
	}
	if o.DPI != nil {
		params["dpi"] = *o.DPI
	}
	if o.Rotation != nil {
		if *o.Rotation%90 != 0 {
			// Rejected locally: the server would 400, and this is unambiguously a caller bug.
			return "", &ValidationError{
				Parameter: "rotation",
				Message:   fmt.Sprintf("Rotation must be a multiple of 90 degrees, but was %d.", *o.Rotation),
			}
		}
		params["rotation"] = *o.Rotation
	}
	if o.Scaling != nil {
		params["scaling"] = *o.Scaling
	}
	if o.ColorMode != nil {
		params["colorMode"] = string(*o.ColorMode)
	}
	if o.Darkness != nil {
		if *o.Darkness < 0 || *o.Darkness > 100 {
			return "", &ValidationError{
				Parameter: "darkness",
				Message:   fmt.Sprintf("Darkness must be between 0 and 100, but was %d.", *o.Darkness),
			}
		}
		params["darkness"] = *o.Darkness
	}
	if o.Position != nil {
		params["position"] = *o.Position
	}
	if o.Watermark != nil {
		params["watermark"] = *o.Watermark
	}
	if o.Dialect != nil {
		params["dialect"] = *o.Dialect
	}
	if o.Data != nil {
		records, err := normalizeData(o.Data)
		if err != nil {
			return "", err
		}
		params["data"] = records
	}
	if o.Label != nil {
		params["label"] = *o.Label
	}
	if o.PDF != nil {
		params["pdf"] = *o.PDF
	}
	if o.ZPL != nil {
		params["zpl"] = *o.ZPL
	}
	for key, value := range o.Extra {
		params[key] = value
	}

	if len(params) == 0 {
		return "", nil
	}

	encoded, err := json.Marshal(params)
	if err != nil {
		return "", fmt.Errorf("labelzoom: could not serialize conversion options: %w", err)
	}
	return string(encoded), nil
}

// normalizeData coerces Options.Data into the array of objects the API expects.
//
// A caller passing a single record means "one label", so a bare object is wrapped rather
// than rejected. An element that is not an object is a caller bug: the keys are the label's
// variable field names, and there is nothing sensible to do with a bare string.
func normalizeData(value any) ([]any, error) {
	// Round-trip through JSON so a caller's own struct, map, or slice type is treated the
	// same as a DataRecord. This is the only place the shape is inspected, and doing it on
	// the encoded form means one rule rather than one per Go type.
	encoded, err := json.Marshal(value)
	if err != nil {
		return nil, &ValidationError{
			Parameter: "data",
			Message:   fmt.Sprintf("data is not JSON-serializable: %v", err),
		}
	}

	var decoded any
	if err := json.Unmarshal(encoded, &decoded); err != nil {
		return nil, &ValidationError{Parameter: "data", Message: fmt.Sprintf("data is not valid JSON: %v", err)}
	}

	records, ok := decoded.([]any)
	if !ok {
		records = []any{decoded}
	}

	for index, record := range records {
		if _, ok := record.(map[string]any); !ok {
			return nil, &ValidationError{
				Parameter: "data",
				Message: fmt.Sprintf(
					"data[%d] is %s; every entry must be an object whose keys are the label's variable field names.",
					index, describe(record)),
			}
		}
	}
	return records, nil
}

func describe(value any) string {
	switch value.(type) {
	case nil:
		return "null"
	case []any:
		return "an array"
	case string:
		return "a string"
	case float64:
		return "a number"
	case bool:
		return "a boolean"
	default:
		return fmt.Sprintf("a %T", value)
	}
}
