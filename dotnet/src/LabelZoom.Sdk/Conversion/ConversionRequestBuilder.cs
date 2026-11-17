using System.IO;
using System.Text;

namespace LabelZoom.Sdk.Conversion
{
    /// <summary>
    /// Chooses the source document. Returned by <see cref="LabelZoomClient.Convert"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A named <c>From*</c> method returns a source stage typed for that format, so only the options
    /// that apply to it are offered: <see cref="FromPdf(byte[])"/> returns a
    /// <see cref="PdfSourceBuilder"/> with <see cref="PdfSourceBuilder.WithPage"/>, and
    /// <see cref="FromZpl"/> doesn't.
    /// </para>
    /// <para>
    /// <see cref="From(SourceFormat, byte[])"/> and the other methods that take a
    /// <see cref="SourceFormat"/> pick the format at run time and return a
    /// <see cref="DynamicSourceBuilder"/>, which offers every source option.
    /// </para>
    /// </remarks>
    public sealed class ConversionRequestBuilder
    {
        private readonly LabelZoomClient _client;

        internal ConversionRequestBuilder(LabelZoomClient client) => _client = client;

        /// <summary>Uses raw bytes as the source document.</summary>
        /// <param name="format">The format those bytes are in.</param>
        /// <param name="body">The document. Must not be empty.</param>
        public DynamicSourceBuilder From(SourceFormat format, byte[] body) =>
            new DynamicSourceBuilder(Draft(format, body));

        /// <summary>Uses text as the source document, encoded as UTF-8.</summary>
        public DynamicSourceBuilder From(SourceFormat format, string body) =>
            new DynamicSourceBuilder(Draft(format, Utf8(body)));

        /// <summary>Reads a stream to completion and uses it as the source document.</summary>
        /// <remarks>
        /// The stream is buffered rather than streamed, because a retried request needs to send
        /// the same body again and a consumed stream cannot be replayed.
        /// </remarks>
        public DynamicSourceBuilder From(SourceFormat format, Stream body) =>
            new DynamicSourceBuilder(Draft(format, ReadAll(body)));

        /// <summary>Reads a file from disk and uses it as the source document.</summary>
        public DynamicSourceBuilder FromFile(SourceFormat format, string path)
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
        public ZplSourceBuilder FromZpl(string zpl) =>
            new ZplSourceBuilder(Draft(SourceFormat.Zpl, Utf8(zpl)));

        /// <summary>Converts from EPL/EPL2.</summary>
        public SourceBuilder FromEpl(string epl) => Text(SourceFormat.Epl, epl);

        /// <summary>Converts from Intermec IPL.</summary>
        public SourceBuilder FromIpl(string ipl) => Text(SourceFormat.Ipl, ipl);

        /// <summary>Converts from TSPL/TSPL2.</summary>
        public SourceBuilder FromTspl(string tspl) => Text(SourceFormat.Tspl, tspl);

        /// <summary>Converts from DPL.</summary>
        public SourceBuilder FromDpl(string dpl) => Text(SourceFormat.Dpl, dpl);

        /// <summary>Converts from SATO SBPL.</summary>
        public SourceBuilder FromSbpl(string sbpl) => Text(SourceFormat.Sbpl, sbpl);

        /// <summary>Converts from LabelZoom XML.</summary>
        public SourceBuilder FromXml(string xml) => Text(SourceFormat.Xml, xml);

        /// <summary>Converts from LabelZoom JSON.</summary>
        public SourceBuilder FromJson(string json) => Text(SourceFormat.Json, json);

        /// <summary>Converts from a PDF document.</summary>
        public PdfSourceBuilder FromPdf(byte[] pdf) =>
            new PdfSourceBuilder(Draft(SourceFormat.Pdf, pdf));

        /// <summary>Converts from a PDF document.</summary>
        public PdfSourceBuilder FromPdf(Stream pdf) =>
            new PdfSourceBuilder(Draft(SourceFormat.Pdf, ReadAll(pdf)));

        /// <summary>Converts from a PNG image.</summary>
        public ImageSourceBuilder FromPng(byte[] png) => Image(SourceFormat.Png, png);

        /// <summary>Converts from a PNG image.</summary>
        public ImageSourceBuilder FromPng(Stream png) => Image(SourceFormat.Png, ReadAll(png));

        /// <summary>Converts from a BMP image.</summary>
        public ImageSourceBuilder FromBmp(byte[] bmp) => Image(SourceFormat.Bmp, bmp);

        /// <summary>Converts from a GIF image.</summary>
        public ImageSourceBuilder FromGif(byte[] gif) => Image(SourceFormat.Gif, gif);

        /// <summary>Converts from a JPEG image.</summary>
        public ImageSourceBuilder FromJpeg(byte[] jpeg) => Image(SourceFormat.Jpeg, jpeg);

        /// <summary>
        /// Uses a base64-encoded document as the source, sent as <c>text/plain</c>.
        /// </summary>
        /// <remarks>
        /// The API accepts PDF and image sources either as raw bytes with their own media type or
        /// as base64 text. Prefer <see cref="From(SourceFormat, byte[])"/> — this exists for
        /// callers whose transport has already base64-encoded the payload for them.
        /// </remarks>
        public DynamicSourceBuilder FromBase64Text(SourceFormat format, string base64)
        {
            if (string.IsNullOrEmpty(base64))
            {
                throw new LabelZoomValidationException(
                    nameof(base64), "Base64 body cannot be null or empty.");
            }

            return new DynamicSourceBuilder(
                new ConversionDraft(_client, format, Encoding.UTF8.GetBytes(base64), "text/plain"));
        }

        /// <summary>
        /// Has the <em>server</em> fetch a URL and convert whatever it finds there.
        /// </summary>
        /// <remarks>
        /// This performs a server-side fetch of a URL you supply. Validate the URL before passing
        /// it if it came from untrusted input.
        /// </remarks>
        public DynamicSourceBuilder FromUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new LabelZoomValidationException(nameof(url), "URL cannot be null or empty.");
            }

            return From(SourceFormat.Url, url);
        }

        private SourceBuilder Text(SourceFormat format, string body) =>
            new SourceBuilder(Draft(format, Utf8(body)));

        private ImageSourceBuilder Image(SourceFormat format, byte[] body) =>
            new ImageSourceBuilder(Draft(format, body));

        private ConversionDraft Draft(SourceFormat format, byte[] body)
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

            return new ConversionDraft(_client, format, body, format.ToMediaType());
        }

        private static byte[] Utf8(string body)
        {
            if (body is null)
            {
                throw new LabelZoomValidationException(nameof(body), "Source body cannot be null.");
            }

            return Encoding.UTF8.GetBytes(body);
        }

        private static byte[] ReadAll(Stream body)
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
            return buffer.ToArray();
        }
    }
}
