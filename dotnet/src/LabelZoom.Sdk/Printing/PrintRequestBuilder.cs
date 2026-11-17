using System.IO;
using System.Text;

namespace LabelZoom.Sdk.Printing
{
    /// <summary>
    /// The first step of a print: choose the document, or a published template, for one printer.
    /// </summary>
    /// <remarks>
    /// The printer's configuration picks the output language, and LabelZoom converts the document to it,
    /// so a PDF prints on a ZPL printer. There are <c>From*</c> methods only for the formats print
    /// converts. For any other format, convert it to the printer's language first and print the result.
    /// </remarks>
    public sealed class PrintRequestBuilder
    {
        private readonly LabelZoomClient _client;
        private readonly string _printerId;

        internal PrintRequestBuilder(LabelZoomClient client, string printerId)
        {
            _client = client;
            _printerId = printerId;
        }

        /// <summary>Prints a document whose format is chosen at run time.</summary>
        /// <exception cref="LabelZoomValidationException">The format can't be printed directly.</exception>
        public DynamicPrintBuilder From(SourceFormat format, byte[] body) =>
            new DynamicPrintBuilder(Draft(PrintWriters.RequirePrintable(format), body));

        /// <summary>Prints a text document whose format is chosen at run time.</summary>
        public DynamicPrintBuilder From(SourceFormat format, string body) =>
            new DynamicPrintBuilder(Draft(PrintWriters.RequirePrintable(format), Utf8(body)));

        /// <summary>Prints a document read from a stream, whose format is chosen at run time.</summary>
        public DynamicPrintBuilder From(SourceFormat format, Stream body) =>
            new DynamicPrintBuilder(Draft(PrintWriters.RequirePrintable(format), ReadAll(body)));

        /// <summary>Prints a file whose format is chosen at run time.</summary>
        public DynamicPrintBuilder FromFile(SourceFormat format, string path)
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

        /// <summary>Prints a ZPL document.</summary>
        public ZplPrintBuilder FromZpl(string zpl) => new ZplPrintBuilder(Draft(SourceFormat.Zpl, Utf8(zpl)));

        /// <summary>Prints an EPL document.</summary>
        public PrinterLanguagePrintBuilder FromEpl(string epl) => PrinterLanguage(SourceFormat.Epl, epl);

        /// <summary>Prints an Intermec IPL document.</summary>
        public PrinterLanguagePrintBuilder FromIpl(string ipl) => PrinterLanguage(SourceFormat.Ipl, ipl);

        /// <summary>Prints a TSC TSPL document.</summary>
        public PrinterLanguagePrintBuilder FromTspl(string tspl) => PrinterLanguage(SourceFormat.Tspl, tspl);

        /// <summary>Prints a Datamax DPL document.</summary>
        public PrinterLanguagePrintBuilder FromDpl(string dpl) => PrinterLanguage(SourceFormat.Dpl, dpl);

        /// <summary>Prints a SATO SBPL document.</summary>
        public PrinterLanguagePrintBuilder FromSbpl(string sbpl) => PrinterLanguage(SourceFormat.Sbpl, sbpl);

        /// <summary>Prints a Honeywell/Intermec Fingerprint or Direct Protocol document.</summary>
        public PrinterLanguagePrintBuilder FromFingerprint(string fingerprint) =>
            PrinterLanguage(SourceFormat.Fingerprint, fingerprint);

        /// <summary>Prints a Zebra CPCL document.</summary>
        public PrinterLanguagePrintBuilder FromCpcl(string cpcl) => PrinterLanguage(SourceFormat.Cpcl, cpcl);

        /// <summary>Prints a LabelZoom XML label.</summary>
        public PrintBuilder FromXml(string xml) => new PrintBuilder(Draft(SourceFormat.Xml, Utf8(xml)));

        /// <summary>Prints a LabelZoom JSON label.</summary>
        public PrintBuilder FromJson(string json) => new PrintBuilder(Draft(SourceFormat.Json, Utf8(json)));

        /// <summary>Prints a PDF document.</summary>
        public PdfPrintBuilder FromPdf(byte[] pdf) => new PdfPrintBuilder(Draft(SourceFormat.Pdf, pdf));

        /// <summary>Prints a PDF document read from a stream.</summary>
        public PdfPrintBuilder FromPdf(Stream pdf) => new PdfPrintBuilder(Draft(SourceFormat.Pdf, ReadAll(pdf)));

        /// <summary>Prints a PNG image.</summary>
        public ImagePrintBuilder FromPng(byte[] png) => Image(SourceFormat.Png, png);

        /// <summary>Prints a PNG image read from a stream.</summary>
        public ImagePrintBuilder FromPng(Stream png) => Image(SourceFormat.Png, ReadAll(png));

        /// <summary>Prints a BMP image.</summary>
        public ImagePrintBuilder FromBmp(byte[] bmp) => Image(SourceFormat.Bmp, bmp);

        /// <summary>Prints a GIF image.</summary>
        public ImagePrintBuilder FromGif(byte[] gif) => Image(SourceFormat.Gif, gif);

        /// <summary>Prints a JPEG image.</summary>
        public ImagePrintBuilder FromJpeg(byte[] jpeg) => Image(SourceFormat.Jpeg, jpeg);

        /// <summary>
        /// Prints a template published from LabelZoom Studio or the web designer. Supply its merge-field
        /// values with <see cref="TemplatePrintBuilder.WithData"/>.
        /// </summary>
        /// <param name="templateId">The template's id, from the Print templates page.</param>
        public TemplatePrintBuilder FromTemplate(string templateId) => new TemplatePrintBuilder(
            _client, _printerId, PrintWriters.RequireId(templateId, nameof(templateId)));

        private PrinterLanguagePrintBuilder PrinterLanguage(SourceFormat format, string body) =>
            new PrinterLanguagePrintBuilder(Draft(format, Utf8(body)));

        private ImagePrintBuilder Image(SourceFormat format, byte[] body) => new ImagePrintBuilder(Draft(format, body));

        private PrintDraft Draft(SourceFormat format, byte[] body)
        {
            if (body is null)
            {
                throw new LabelZoomValidationException(nameof(body), "The document cannot be null.");
            }

            if (body.Length == 0)
            {
                throw new LabelZoomValidationException(
                    nameof(body), "The document cannot be empty; the API rejects an empty print request.");
            }

            return new PrintDraft(_client, _printerId, format, body);
        }

        private static byte[] Utf8(string body)
        {
            if (body is null)
            {
                throw new LabelZoomValidationException(nameof(body), "The document cannot be null.");
            }

            return Encoding.UTF8.GetBytes(body);
        }

        private static byte[] ReadAll(Stream body)
        {
            if (body is null)
            {
                throw new LabelZoomValidationException(nameof(body), "The document stream cannot be null.");
            }

            if (!body.CanRead)
            {
                throw new LabelZoomValidationException(nameof(body), "The document stream is not readable.");
            }

            using var buffer = new MemoryStream();
            body.CopyTo(buffer);
            return buffer.ToArray();
        }
    }
}
