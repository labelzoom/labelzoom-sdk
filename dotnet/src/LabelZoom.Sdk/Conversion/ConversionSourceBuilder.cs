namespace LabelZoom.Sdk.Conversion
{
    /// <summary>
    /// Chooses the target format. One class covers all eleven targets.
    /// </summary>
    /// <remarks>
    /// There is no <c>ToUrl</c>: <c>URL</c> is a source-only fetch instruction, and
    /// <see cref="TargetFormat"/> has no member for it. Attempting one is a compile error rather
    /// than a runtime 404.
    /// </remarks>
    public sealed class ConversionSourceBuilder
    {
        private readonly LabelZoomClient _client;
        private readonly SourceFormat _source;
        private readonly byte[] _body;
        private readonly string _contentType;
        private readonly ConversionParameters _parameters;

        internal ConversionSourceBuilder(
            LabelZoomClient client, SourceFormat source, byte[] body, string contentType)
        {
            _client = client;
            _source = source;
            _body = body;
            _contentType = contentType;
            _parameters = new ConversionParameters();
        }

        /// <summary>
        /// How the <b>source's</b> absolute positions are interpreted, in dots per inch: dots for
        /// printer languages, pixels for bitmap images, an override of the document's dpi for
        /// LabelZoom XML/JSON. Not applicable to PDF sources (vector). This is the source-side dpi;
        /// use <see cref="ConversionTargetBuilder.WithDpi"/> to author the output at a resolution,
        /// and both may be set when the chosen format pair supports a dpi on each side.
        /// </summary>
        public ConversionSourceBuilder WithDpi(int dpi)
        {
            if (dpi <= 0)
            {
                throw new LabelZoomValidationException(nameof(dpi), "DPI must be greater than zero.");
            }

            _parameters.Set("sourceDpi", dpi);
            return this;
        }

        /// <summary>Selects the target format.</summary>
        public ConversionTargetBuilder To(TargetFormat target) =>
            new ConversionTargetBuilder(_client, _source, target, _body, _contentType, _parameters);

        /// <summary>Converts to ZPL. All labels are concatenated into one document.</summary>
        public ConversionTargetBuilder ToZpl() => To(TargetFormat.Zpl);

        /// <summary>
        /// Converts to EPL. All labels are concatenated into one document. Read
        /// <see cref="ConversionResult.Bytes"/> rather than <see cref="ConversionResult.Text"/>:
        /// EPL's <c>GW</c> command inlines raw binary.
        /// </summary>
        public ConversionTargetBuilder ToEpl() => To(TargetFormat.Epl);

        /// <summary>
        /// Converts to TSPL. All labels are concatenated into one document. As with
        /// <see cref="ToEpl"/>, prefer <see cref="ConversionResult.Bytes"/>.
        /// </summary>
        public ConversionTargetBuilder ToTspl() => To(TargetFormat.Tspl);

        /// <summary>Converts to Datamax DPL. All labels are concatenated into one document.</summary>
        public ConversionTargetBuilder ToDpl() => To(TargetFormat.Dpl);

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
