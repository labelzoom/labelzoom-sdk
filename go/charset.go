package labelzoom

import (
	"mime"
	"strings"
	"unicode/utf8"
)

// decodeText decodes body using the charset named in contentType, defaulting to UTF-8.
//
// Not a hardcoded UTF-8 decode: the API serves ISO-8859-1 for some conversions, and a byte
// like 0xE9 is not valid UTF-8 -- it would come back as U+FFFD rather than "é".
//
// Handled here rather than through golang.org/x/text/encoding on purpose. That module is the
// complete and correct answer, and it is a multi-megabyte dependency tree with its own
// release cadence, added to a library whose one notable property is that it has none. The
// four labels below are what an HTTP API actually serves; anything else falls back to UTF-8,
// which is the same thing every other LabelZoom SDK does for a charset its runtime does not
// recognize.
func decodeText(body []byte, contentType string) string {
	switch normalizeCharset(charsetOf(contentType)) {
	case "", "utf-8", "us-ascii":
		// Replace invalid sequences rather than returning them: the result is a Go string,
		// which is expected to be valid UTF-8.
		return strings.ToValidUTF8(string(body), string(utf8.RuneError))
	case "iso-8859-1":
		return decodeLatin1(body)
	case "windows-1252":
		return decodeWindows1252(body)
	default:
		// An unrecognized charset is not worth failing an otherwise good conversion.
		return strings.ToValidUTF8(string(body), string(utf8.RuneError))
	}
}

// decodeLatin1 maps every byte to the code point of the same value, which is the whole of
// ISO-8859-1 -- its 256 characters are exactly the first 256 Unicode code points.
func decodeLatin1(body []byte) string {
	var out strings.Builder
	out.Grow(len(body))
	for _, b := range body {
		out.WriteRune(rune(b))
	}
	return out.String()
}

// windows1252Extras is the 0x80-0x9F range, the only place Windows-1252 differs from
// ISO-8859-1. Worth carrying because servers label Windows-1252 as ISO-8859-1 and vice
// versa constantly, and getting it wrong turns a smart quote into a control character.
var windows1252Extras = [32]rune{
	0x20AC, 0xFFFD, 0x201A, 0x0192, 0x201E, 0x2026, 0x2020, 0x2021,
	0x02C6, 0x2030, 0x0160, 0x2039, 0x0152, 0xFFFD, 0x017D, 0xFFFD,
	0xFFFD, 0x2018, 0x2019, 0x201C, 0x201D, 0x2022, 0x2013, 0x2014,
	0x02DC, 0x2122, 0x0161, 0x203A, 0x0153, 0xFFFD, 0x017E, 0x0178,
}

func decodeWindows1252(body []byte) string {
	var out strings.Builder
	out.Grow(len(body))
	for _, b := range body {
		if b >= 0x80 && b <= 0x9F {
			out.WriteRune(windows1252Extras[b-0x80])
			continue
		}
		out.WriteRune(rune(b))
	}
	return out.String()
}

// charsetOf pulls the charset parameter off a Content-Type header.
func charsetOf(contentType string) string {
	if contentType == "" {
		return ""
	}
	_, params, err := mime.ParseMediaType(contentType)
	if err != nil {
		return ""
	}
	return params["charset"]
}

// normalizeCharset folds the aliases that show up in the wild onto one label each.
func normalizeCharset(charset string) string {
	switch strings.ToLower(strings.TrimSpace(charset)) {
	case "":
		return ""
	case "utf-8", "utf8":
		return "utf-8"
	case "us-ascii", "ascii":
		return "us-ascii"
	case "iso-8859-1", "iso8859-1", "iso_8859-1", "latin-1", "latin1", "l1", "cp819":
		return "iso-8859-1"
	case "windows-1252", "cp1252", "cp-1252":
		return "windows-1252"
	default:
		return "unsupported"
	}
}
