// Package labelzoom is the official Go client for the LabelZoom API.
//
// LabelZoom converts barcode labels between printer languages (ZPL, EPL, TSPL, DPL),
// LabelZoom's own XML/JSON model, PDF, and raster images. Almost everything the API does
// happens at one endpoint:
//
//	POST https://api.labelzoom.com/api/v2/convert/{sourceFormat}/to/{targetFormat}
//
// Authentication is optional. Without a credential the API serves a free tier: watermarked
// output, the first label only, a 1 MB request cap, and no multi-page, JSON-target or
// image-to-image conversion. Constructing a Client with no key is therefore a supported,
// tested path rather than an error.
//
//	client, err := labelzoom.New(labelzoom.WithAPIKey("lz_live_..."))
//	if err != nil {
//	    return err
//	}
//	result, err := client.Convert(ctx, labelzoom.ConvertRequest{
//	    From: labelzoom.SourceZPL,
//	    To:   labelzoom.TargetPNG,
//	    Body: []byte("^XA^FO20,20^A0N,28^FDhello^FS^XZ"),
//	})
//	if err != nil {
//	    return err
//	}
//	os.WriteFile("label.png", result.Bytes, 0o644)
//
// The behaviour of every LabelZoom SDK is specified in docs/API_CONTRACT.md and checked by
// the shared fixtures in conformance/, which this package's test suite executes in full.
package labelzoom

// Version is the SDK version. It appears in the User-Agent of every request, so the
// release workflow asserts that it matches the `go/vX.Y.Z` tag being published.
const Version = "1.0.0"

// DefaultBaseURL is the production API host.
const DefaultBaseURL = "https://api.labelzoom.com"

// APIKeyEnvVar is the environment variable consulted when no credential is configured.
const APIKeyEnvVar = "LABELZOOM_API_KEY"

// requestIDHeader is the support handle the gateway stamps on every response. Go
// canonicalizes header names on lookup, so the casing here is only cosmetic -- which
// matters, because CORS exposes the same header as X-LZ-Request-ID.
const requestIDHeader = "X-LZ-Request-Id"

// Ptr returns a pointer to v, for setting an [Options] field.
//
// Options are pointers so that "not set" is distinguishable from a zero value the caller
// meant: Watermark: Ptr(false) and PageNumber: Ptr(0) are both sent, while an omitted field
// is not sent at all. The SDK never substitutes a default of its own, so a change to a
// server default reaches you without an SDK upgrade.
//
// Type inference follows the literal, so a float64 field wants Ptr(4.0), not Ptr(4).
func Ptr[T any](v T) *T { return &v }
