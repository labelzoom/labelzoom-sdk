package labelzoom

import (
	"fmt"
	"os"
)

// Result is the outcome of a successful conversion.
//
// Bytes is authoritative. PDF, PNG, BMP, GIF and JPEG targets are binary, so treating the
// response as a string would silently corrupt five of the eleven targets -- and EPL and
// TSPL reach the same hazard through a text/plain response, because their GW and BITMAP
// commands inline a raw 1-bpp payload.
type Result struct {
	// Bytes is the response body, exactly as the server sent it.
	Bytes []byte
	// ContentType is the response Content-Type header, if any.
	ContentType string
	// Status is the HTTP status code, always 2xx here.
	Status int
	// RequestID is the X-LZ-Request-Id response header, when the server sent one. The
	// support handle.
	RequestID string
}

// Text decodes Bytes using the response charset, defaulting to UTF-8.
//
// Safe for the textual targets. For a binary target, or an EPL/TSPL label that might carry
// graphics, read Bytes instead.
func (r *Result) Text() string {
	return decodeText(r.Bytes, r.ContentType)
}

// Save writes Bytes to path.
func (r *Result) Save(path string) error {
	if err := os.WriteFile(path, r.Bytes, 0o600); err != nil {
		return fmt.Errorf("labelzoom: could not save conversion result: %w", err)
	}
	return nil
}
