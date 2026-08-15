using System;
using System.IO;
using System.Text;

namespace LabelZoom.Sdk.Conversion
{
    /// <summary>
    /// Chooses the source document. Returned by <see cref="LabelZoomClient.Convert"/>.
    /// </summary>
    /// <remarks>
    /// There is one source builder and one target builder for all 12 x 8 format combinations, not
    /// a class per format. The named <c>From*</c> methods below are one-line delegations to
    /// <see cref="From(SourceFormat, byte[])"/>: they exist for discoverability and, holding no
    /// logic of their own, cannot drift away from the format table.
    /// </remarks>
    public sealed class ConversionRequestBuilder
    {
        private readonly LabelZoomClient _client;

        internal ConversionRequestBuilder(LabelZoomClient client) => _client = client;

        /// <summary>Uses raw bytes as the source document.</summary>
        /// <param name="format">The format those bytes are in.</param>
        /// <param name="body">The document. Must not be empty.</param>
        public ConversionSourceBuilder From(SourceFormat format, byte[] body)
        {
            if (body is null)
            {
                throw new LabelZoomValidationException(nameof(body), "Source body cannot be null.");
            }

            if (body.Length == 0)
            {
                // The gateway rejects a zero-length body with 400. Catching it here saves a round
                // trip and says something more useful than "Request body is required".
                throw new LabelZoomValidationException(
                    nameof(body), "Source body cannot be empty; the API rejects zero-length requests.");
            }

            return new ConversionSourceBuilder(_client, format, body, format.ToMediaType());
        }

        /// <summary>Uses text as the source document, encoded as UTF-8.</summary>
        public ConversionSourceBuilder From(SourceFormat format, string body)
        {
            if (body is null)
            {
                throw new LabelZoomValidationException(nameof(body), "Source body cannot be null.");
            }

            return From(format, Encoding.UTF8.GetBytes(body));
        }

        /// <summary>Reads a stream to completion and uses it as the source document.</summary>
        /// <remarks>
        /// The stream is buffered rather than streamed, because a retried request needs to send
        /// the same body again and a consumed stream cannot be replayed.
        /// </remarks>
        public ConversionSourceBuilder From(SourceFormat format, Stream body)
        {
            if (body is null)
            {
                throw new LabelZoomValidationException(nameof(body), "Source stream cannot be null.");
            }

            if (!body.CanRead)
            {
                throw new LabelZoomValidationException(nameof(body), "Source stream is not readable.");
            }

            using var buffer = new MemoryStream();
            body.CopyTo(buffer);
            return From(format, buffer.ToArray());
        }

        /// <summary>Reads a file from disk and uses it as the source document.</summary>
        public ConversionSourceBuilder FromFile(SourceFormat format, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new LabelZoomValidationException(nameof(path), "Path cannot be null or empty.");
            }

            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Source file not found: {path}", path);
            }

            return From(format, File.ReadAllBytes(path));
        }

        /// <summary>Converts from ZPL.</summary>
        public ConversionSourceBuilder FromZpl(string zpl) => From(SourceFormat.Zpl, zpl);

        /// <summary>Converts from EPL/EPL2. Source-only on the server.</summary>
        public ConversionSourceBuilder FromEpl(string epl) => From(SourceFormat.Epl, epl);

        /// <summary>Converts from TSPL/TSPL2. Source-only on the server.</summary>
        public ConversionSourceBuilder FromTspl(string tspl) => From(SourceFormat.Tspl, tspl);

        /// <summary>Converts from DPL. Source-only on the server.</summary>
        public ConversionSourceBuilder FromDpl(string dpl) => From(SourceFormat.Dpl, dpl);

        /// <summary>Converts from LabelZoom XML.</summary>
        public ConversionSourceBuilder FromXml(string xml) => From(SourceFormat.Xml, xml);

        /// <summary>Converts from LabelZoom JSON.</summary>
        public ConversionSourceBuilder FromJson(string json) => From(SourceFormat.Json, json);

        /// <summary>Converts from a PDF document.</summary>
        public ConversionSourceBuilder FromPdf(byte[] pdf) => From(SourceFormat.Pdf, pdf);

        /// <summary>Converts from a PDF document.</summary>
        public ConversionSourceBuilder FromPdf(Stream pdf) => From(SourceFormat.Pdf, pdf);

        /// <summary>Converts from a PNG image.</summary>
        public ConversionSourceBuilder FromPng(byte[] png) => From(SourceFormat.Png, png);

        /// <summary>Converts from a PNG image.</summary>
        public ConversionSourceBuilder FromPng(Stream png) => From(SourceFormat.Png, png);

        /// <summary>Converts from a BMP image.</summary>
        public ConversionSourceBuilder FromBmp(byte[] bmp) => From(SourceFormat.Bmp, bmp);

        /// <summary>Converts from a GIF image.</summary>
        public ConversionSourceBuilder FromGif(byte[] gif) => From(SourceFormat.Gif, gif);

        /// <summary>Converts from a JPEG image.</summary>
        public ConversionSourceBuilder FromJpeg(byte[] jpeg) => From(SourceFormat.Jpeg, jpeg);

        /// <summary>
        /// Uses a base64-encoded document as the source, sent as <c>text/plain</c>.
        /// </summary>
        /// <remarks>
        /// The API accepts PDF and image sources either as raw bytes with their own media type or
        /// as base64 text. Prefer <see cref="From(SourceFormat, byte[])"/> — this exists for
        /// callers whose transport has already base64-encoded the payload for them.
        /// </remarks>
        public ConversionSourceBuilder FromBase64Text(SourceFormat format, string base64)
        {
            if (string.IsNullOrEmpty(base64))
            {
                throw new LabelZoomValidationException(
                    nameof(base64), "Base64 body cannot be null or empty.");
            }

            return new ConversionSourceBuilder(
                _client, format, Encoding.UTF8.GetBytes(base64), "text/plain");
        }

        /// <summary>
        /// Has the <em>server</em> fetch a URL and convert whatever it finds there.
        /// </summary>
        /// <remarks>
        /// This performs a server-side fetch of a URL you supply. Validate the URL before passing
        /// it if it came from untrusted input.
        /// </remarks>
        public ConversionSourceBuilder FromUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new LabelZoomValidationException(nameof(url), "URL cannot be null or empty.");
            }

            return From(SourceFormat.Url, url);
        }
    }
}
