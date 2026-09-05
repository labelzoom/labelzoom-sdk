package labelzoom

import "fmt"

// SourceFormat is the format of the document being converted.
//
// It is a distinct type from [TargetFormat], and the two sets are not the same: JPG and URL
// are source-only. JPG is an input spelling that normalizes to JPEG on the wire, and URL is
// an instruction to the server to go fetch a document rather than a format at all. Passing a
// SourceFormat where a TargetFormat is wanted does not compile.
type SourceFormat string

// TargetFormat is the format to convert into.
//
// There is no TargetURL: URL is a fetch instruction, not an output format. The printer
// languages round-trip -- EPL, IPL, TSPL, DPL and SBPL are targets as well as sources.
type TargetFormat string

// The fourteen document formats, plus URL.
const (
	SourceZPL  SourceFormat = "zpl"
	SourceEPL  SourceFormat = "epl"
	SourceIPL  SourceFormat = "ipl"
	SourceTSPL SourceFormat = "tspl"
	SourceDPL  SourceFormat = "dpl"
	SourceSBPL SourceFormat = "sbpl"
	SourceXML  SourceFormat = "xml"
	SourceJSON SourceFormat = "json"
	SourcePDF  SourceFormat = "pdf"
	SourcePNG  SourceFormat = "png"
	SourceBMP  SourceFormat = "bmp"
	SourceGIF  SourceFormat = "gif"
	SourceJPEG SourceFormat = "jpeg"
	// SourceJPG is an alias for SourceJPEG and is sent as "jpeg".
	SourceJPG SourceFormat = "jpg"
	// SourceURL has the server fetch a document and convert what it finds. The request body
	// is the URL. Validate it first if it came from untrusted input: the fetch happens
	// server-side, from LabelZoom's network.
	SourceURL SourceFormat = "url"
)

// The thirteen output formats.
const (
	TargetZPL TargetFormat = "zpl"
	// TargetEPL output can inline raw binary (the GW command); read Result.Bytes rather
	// than Result.Text when a label might carry graphics.
	TargetEPL TargetFormat = "epl"
	// TargetIPL frames commands in STX/ETX and carries graphics as packed bitmap columns;
	// read Result.Bytes rather than Result.Text when a label might carry graphics.
	TargetIPL TargetFormat = "ipl"
	// TargetTSPL output can inline raw binary (the BITMAP command); read Result.Bytes
	// rather than Result.Text when a label might carry graphics.
	TargetTSPL TargetFormat = "tspl"
	TargetDPL  TargetFormat = "dpl"
	// TargetSBPL frames every command with ESC and embeds graphics inline; read
	// Result.Bytes rather than Result.Text when a label might carry graphics.
	TargetSBPL TargetFormat = "sbpl"
	TargetXML  TargetFormat = "xml"
	TargetJSON TargetFormat = "json"
	TargetPDF  TargetFormat = "pdf"
	TargetPNG  TargetFormat = "png"
	TargetBMP  TargetFormat = "bmp"
	TargetGIF  TargetFormat = "gif"
	TargetJPEG TargetFormat = "jpeg"
)

// SourceFormats lists every accepted source, in the contract's order.
func SourceFormats() []SourceFormat {
	return []SourceFormat{
		SourceZPL, SourceEPL, SourceIPL, SourceTSPL, SourceDPL, SourceSBPL,
		SourceXML, SourceJSON, SourcePDF, SourcePNG, SourceBMP, SourceGIF,
		SourceJPEG, SourceJPG, SourceURL,
	}
}

// TargetFormats lists every accepted target, in the contract's order.
func TargetFormats() []TargetFormat {
	return []TargetFormat{
		TargetZPL, TargetEPL, TargetIPL, TargetTSPL, TargetDPL, TargetSBPL,
		TargetXML, TargetJSON, TargetPDF, TargetPNG, TargetBMP, TargetGIF,
		TargetJPEG,
	}
}

// sourceMediaTypes is the format metadata table, defined once.
//
// It is deliberately the only place a format's media type appears. The superseded .NET
// design had a builder class per format, each independently knowing this mapping, and they
// drifted -- one of them emitted the source type as the target.
var sourceMediaTypes = map[SourceFormat]string{
	SourceZPL:  "text/plain",
	SourceEPL:  "text/plain",
	SourceIPL:  "text/plain",
	SourceTSPL: "text/plain",
	SourceDPL:  "text/plain",
	SourceSBPL: "text/plain",
	SourceXML:  "application/xml",
	SourceJSON: "application/json",
	SourcePDF:  "application/pdf",
	SourcePNG:  "image/png",
	SourceBMP:  "image/bmp",
	SourceGIF:  "image/gif",
	SourceJPEG: "image/jpeg",
	SourceJPG:  "image/jpeg",
	// The body is the URL itself.
	SourceURL: "text/plain",
}

// wireToken is the path segment for this source. JPG normalizes to "jpeg".
func (f SourceFormat) wireToken() string {
	if f == SourceJPG {
		return string(SourceJPEG)
	}
	return string(f)
}

// mediaType is the request Content-Type for this source.
func (f SourceFormat) mediaType() (string, error) {
	mediaType, ok := sourceMediaTypes[f]
	if !ok {
		return "", &ValidationError{
			Parameter: "from",
			Message:   fmt.Sprintf("%q is not a source format the LabelZoom API accepts.", string(f)),
		}
	}
	return mediaType, nil
}

// wireToken is the path segment for this target. Targets need no normalization.
func (f TargetFormat) wireToken() string { return string(f) }

func (f TargetFormat) validate() error {
	for _, known := range TargetFormats() {
		if f == known {
			return nil
		}
	}
	return &ValidationError{
		Parameter: "to",
		Message:   fmt.Sprintf("%q is not a target format the LabelZoom API produces.", string(f)),
	}
}

// ColorMode selects how colour is reduced when rasterizing. Server default GRAYSCALE.
type ColorMode string

// The colour modes the API accepts.
const (
	ColorModeBW        ColorMode = "BW"
	ColorModeGrayscale ColorMode = "GRAYSCALE"
	ColorModeColor     ColorMode = "COLOR"
)

// PDFConversionMode selects how a PDF source is interpreted. Server default IMAGE.
type PDFConversionMode string

// The PDF conversion modes the API accepts.
const (
	// PDFConversionModeImage rasterizes the page.
	PDFConversionModeImage PDFConversionMode = "IMAGE"
	// PDFConversionModeNative extracts the page's text and vectors.
	PDFConversionModeNative PDFConversionMode = "NATIVE"
)

// ZPLImageCompression selects the encoding of images embedded in ZPL output.
// Server default Z64.
type ZPLImageCompression string

// The ZPL image compressions the API accepts.
const (
	ZPLImageCompressionZ64           ZPLImageCompression = "Z64"
	ZPLImageCompressionCompressedHex ZPLImageCompression = "COMPRESSED_HEX"
)
