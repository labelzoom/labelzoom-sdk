using System;
using System.Collections.Generic;

namespace LabelZoom.Sdk
{
    /// <summary>
    /// A format the LabelZoom API can convert <em>from</em>.
    /// </summary>
    /// <remarks>
    /// Deliberately a different type from <see cref="TargetFormat"/>. That is what makes
    /// <see cref="Epl"/>, <see cref="Tspl"/> and <see cref="Dpl"/> un-selectable as conversion
    /// targets: they are source-only on the server, and there is simply no
    /// <c>TargetFormat.Epl</c> to name.
    /// </remarks>
    public enum SourceFormat
    {
        /// <summary>Zebra Programming Language. Sent as <c>text/plain</c>.</summary>
        Zpl,

        /// <summary>Eltron Programming Language. Source-only. Sent as <c>text/plain</c>.</summary>
        Epl,

        /// <summary>TSC Printer Language. Source-only. Sent as <c>text/plain</c>.</summary>
        Tspl,

        /// <summary>Datamax Printer Language. Source-only. Sent as <c>text/plain</c>.</summary>
        Dpl,

        /// <summary>LabelZoom XML. Sent as <c>application/xml</c>.</summary>
        Xml,

        /// <summary>LabelZoom JSON. Sent as <c>application/json</c>.</summary>
        Json,

        /// <summary>PDF document. Sent as <c>application/pdf</c>.</summary>
        Pdf,

        /// <summary>PNG image. Sent as <c>image/png</c>.</summary>
        Png,

        /// <summary>BMP image. Sent as <c>image/bmp</c>.</summary>
        Bmp,

        /// <summary>GIF image. Sent as <c>image/gif</c>.</summary>
        Gif,

        /// <summary>JPEG image. Sent as <c>image/jpeg</c>.</summary>
        Jpeg,

        /// <summary>Alias for <see cref="Jpeg"/>; normalized to <c>jpeg</c> on the wire.</summary>
        Jpg,

        /// <summary>
        /// A URL, sent as the request body with <c>text/plain</c>. The <em>server</em> then fetches
        /// that URL and converts whatever it finds.
        /// </summary>
        /// <remarks>
        /// This hands a caller-supplied URL to a server-side fetch. Do not pass a URL derived from
        /// untrusted input without validating it first.
        /// </remarks>
        Url,
    }

    /// <summary>
    /// A format the LabelZoom API can convert <em>to</em>.
    /// </summary>
    /// <remarks>
    /// EPL, TSPL and DPL are intentionally absent — the server accepts them as sources only.
    /// </remarks>
    public enum TargetFormat
    {
        /// <summary>Zebra Programming Language. All labels are concatenated.</summary>
        Zpl,

        /// <summary>LabelZoom XML. First label only.</summary>
        Xml,

        /// <summary>LabelZoom JSON. First label only. Requires a paid license.</summary>
        Json,

        /// <summary>PDF document, one page per label.</summary>
        Pdf,

        /// <summary>PNG image. First label only.</summary>
        Png,

        /// <summary>BMP image. First label only.</summary>
        Bmp,

        /// <summary>GIF image. First label only.</summary>
        Gif,

        /// <summary>JPEG image. First label only.</summary>
        Jpeg,
    }

    /// <summary>Colour handling when rasterizing or tracing images.</summary>
    public enum ColorMode
    {
        /// <summary>Two-colour black and white.</summary>
        Bw,

        /// <summary>Greyscale. The server default.</summary>
        Grayscale,

        /// <summary>Full colour.</summary>
        Color,
    }

    /// <summary>How a source PDF is interpreted.</summary>
    public enum PdfConversionMode
    {
        /// <summary>Rasterize the page and trace it. The server default.</summary>
        Image,

        /// <summary>Read the PDF's native drawing operations.</summary>
        Native,
    }

    /// <summary>Image compression used when writing ZPL.</summary>
    public enum ZplImageCompression
    {
        /// <summary>Base-64 encoded, DEFLATE compressed. The server default.</summary>
        Z64,

        /// <summary>Run-length encoded hexadecimal.</summary>
        CompressedHex,
    }

    /// <summary>
    /// Wire tokens and media types for <see cref="SourceFormat"/> and <see cref="TargetFormat"/>.
    /// </summary>
    /// <remarks>
    /// This is the single place in the SDK that knows the format matrix. The superseded builder
    /// hierarchy spread the same knowledge across seven classes, and they drifted — one of them
    /// handled 2 of 12 sources, another returned the source content type as the target format.
    /// Keep it here.
    /// </remarks>
    internal static class Formats
    {
        private static readonly Dictionary<SourceFormat, string> SourceTokens =
            new Dictionary<SourceFormat, string>
            {
                [SourceFormat.Zpl] = "zpl",
                [SourceFormat.Epl] = "epl",
                [SourceFormat.Tspl] = "tspl",
                [SourceFormat.Dpl] = "dpl",
                [SourceFormat.Xml] = "xml",
                [SourceFormat.Json] = "json",
                [SourceFormat.Pdf] = "pdf",
                [SourceFormat.Png] = "png",
                [SourceFormat.Bmp] = "bmp",
                [SourceFormat.Gif] = "gif",
                // Rule A2: jpg normalizes to jpeg. One canonical wire spelling.
                [SourceFormat.Jpeg] = "jpeg",
                [SourceFormat.Jpg] = "jpeg",
                [SourceFormat.Url] = "url",
            };

        private static readonly Dictionary<SourceFormat, string> SourceMediaTypes =
            new Dictionary<SourceFormat, string>
            {
                [SourceFormat.Zpl] = "text/plain",
                [SourceFormat.Epl] = "text/plain",
                [SourceFormat.Tspl] = "text/plain",
                [SourceFormat.Dpl] = "text/plain",
                [SourceFormat.Xml] = "application/xml",
                [SourceFormat.Json] = "application/json",
                [SourceFormat.Pdf] = "application/pdf",
                [SourceFormat.Png] = "image/png",
                [SourceFormat.Bmp] = "image/bmp",
                [SourceFormat.Gif] = "image/gif",
                [SourceFormat.Jpeg] = "image/jpeg",
                [SourceFormat.Jpg] = "image/jpeg",
                [SourceFormat.Url] = "text/plain",
            };

        private static readonly Dictionary<TargetFormat, string> TargetTokens =
            new Dictionary<TargetFormat, string>
            {
                [TargetFormat.Zpl] = "zpl",
                [TargetFormat.Xml] = "xml",
                [TargetFormat.Json] = "json",
                [TargetFormat.Pdf] = "pdf",
                [TargetFormat.Png] = "png",
                [TargetFormat.Bmp] = "bmp",
                [TargetFormat.Gif] = "gif",
                [TargetFormat.Jpeg] = "jpeg",
            };

        internal static string ToWireToken(this SourceFormat format) =>
            SourceTokens.TryGetValue(format, out var token)
                ? token
                : throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown source format.");

        internal static string ToMediaType(this SourceFormat format) =>
            SourceMediaTypes.TryGetValue(format, out var mediaType)
                ? mediaType
                : throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown source format.");

        internal static string ToWireToken(this TargetFormat format) =>
            TargetTokens.TryGetValue(format, out var token)
                ? token
                : throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown target format.");

        internal static string ToWireToken(this ColorMode mode) =>
            mode switch
            {
                ColorMode.Bw => "BW",
                ColorMode.Grayscale => "GRAYSCALE",
                ColorMode.Color => "COLOR",
                _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
            };

        internal static string ToWireToken(this PdfConversionMode mode) =>
            mode switch
            {
                PdfConversionMode.Image => "IMAGE",
                PdfConversionMode.Native => "NATIVE",
                _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
            };

        internal static string ToWireToken(this ZplImageCompression compression) =>
            compression switch
            {
                ZplImageCompression.Z64 => "Z64",
                ZplImageCompression.CompressedHex => "COMPRESSED_HEX",
                _ => throw new ArgumentOutOfRangeException(nameof(compression), compression, null),
            };
    }
}
