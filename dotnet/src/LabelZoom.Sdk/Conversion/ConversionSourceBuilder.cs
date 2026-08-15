namespace LabelZoom.Sdk.Conversion
{
    /// <summary>
    /// Chooses the target format. One class covers all eight targets.
    /// </summary>
    /// <remarks>
    /// There is no <c>ToEpl</c>, <c>ToTspl</c> or <c>ToDpl</c>, and there never will be: those
    /// formats are source-only on the server, and <see cref="TargetFormat"/> has no member for
    /// them. Attempting one is a compile error rather than a runtime 404.
    /// </remarks>
    public sealed class ConversionSourceBuilder
    {
        private readonly LabelZoomClient _client;
        private readonly SourceFormat _source;
        private readonly byte[] _body;
        private readonly string _contentType;

        internal ConversionSourceBuilder(
            LabelZoomClient client, SourceFormat source, byte[] body, string contentType)
        {
            _client = client;
            _source = source;
            _body = body;
            _contentType = contentType;
        }

        /// <summary>Selects the target format.</summary>
        public ConversionTargetBuilder To(TargetFormat target) =>
            new ConversionTargetBuilder(_client, _source, target, _body, _contentType);

        /// <summary>Converts to ZPL. All labels are concatenated into one document.</summary>
        public ConversionTargetBuilder ToZpl() => To(TargetFormat.Zpl);

        /// <summary>Converts to LabelZoom XML. Returns the first label only.</summary>
        public ConversionTargetBuilder ToXml() => To(TargetFormat.Xml);

        /// <summary>Converts to LabelZoom JSON. Returns the first label only; requires a paid license.</summary>
        public ConversionTargetBuilder ToJson() => To(TargetFormat.Json);

        /// <summary>Converts to PDF, one page per label.</summary>
        public ConversionTargetBuilder ToPdf() => To(TargetFormat.Pdf);

        /// <summary>Converts to a PNG image. Returns the first label only.</summary>
        public ConversionTargetBuilder ToPng() => To(TargetFormat.Png);

        /// <summary>Converts to a BMP image. Returns the first label only.</summary>
        public ConversionTargetBuilder ToBmp() => To(TargetFormat.Bmp);

        /// <summary>Converts to a GIF image. Returns the first label only.</summary>
        public ConversionTargetBuilder ToGif() => To(TargetFormat.Gif);

        /// <summary>Converts to a JPEG image. Returns the first label only.</summary>
        public ConversionTargetBuilder ToJpeg() => To(TargetFormat.Jpeg);
    }
}
