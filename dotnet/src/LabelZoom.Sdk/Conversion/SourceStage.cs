namespace LabelZoom.Sdk.Conversion
{
    /// <summary>
    /// The source stage: options that change how the source document is <b>read</b>, followed by
    /// the choice of target format.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A conversion is built in two stages. <c>From*</c> picks the source and returns a source-stage
    /// builder; <c>To*</c> picks the target and returns a target-stage builder that executes the
    /// request. Each stage offers only the options that act on its side of the conversion.
    /// </para>
    /// <para>
    /// The options here apply to every source format. Format-specific ones live on the builder
    /// that <c>From*</c> returns for that format, such as <see cref="PdfSourceBuilder.WithPage"/>.
    /// </para>
    /// </remarks>
    /// <typeparam name="TSelf">The concrete builder, so each method returns it unchanged.</typeparam>
    public abstract class SourceStage<TSelf>
        where TSelf : SourceStage<TSelf>
    {
        private protected SourceStage(ConversionDraft draft) => Draft = draft;

        private protected ConversionDraft Draft { get; }

        private protected ConversionParameters Parameters => Draft.Parameters;

        private protected TSelf Self => (TSelf)this;

        /// <summary>
        /// Label dimensions <b>in inches</b>. They replace whatever size the source document
        /// declares; omit this to use the declared size, or to detect one from the content.
        /// </summary>
        /// <param name="widthInches">Width in inches — not dots, not millimeters.</param>
        /// <param name="heightInches">Height in inches.</param>
        public TSelf WithLabelSize(float widthInches, float heightInches)
        {
            ParameterWriters.LabelSize(Parameters, widthInches, heightInches);
            return Self;
        }

        /// <summary>
        /// Converts only one label of a multi-label source, on every plan, identified by a
        /// <b>0-based</b> index. For an image or PDF it counts labels after any split, page by page.
        /// </summary>
        /// <param name="zeroBasedIndex">0 is the first label.</param>
        public TSelf WithLabelIndex(int zeroBasedIndex)
        {
            ParameterWriters.LabelIndex(Parameters, zeroBasedIndex);
            return Self;
        }

        /// <summary>
        /// Supplies data to fill the label's variable fields. <b>Each record produces one label.</b>
        /// </summary>
        /// <param name="records">
        /// One or more objects whose properties are the label's variable field names. Anonymous
        /// types and dictionaries both work. A single record means a single label.
        /// </param>
        public TSelf WithData(params object?[] records)
        {
            ParameterWriters.Data(Parameters, records);
            return Self;
        }

        /// <summary>Selects the target format at run time. Every target option is available.</summary>
        public DynamicTargetBuilder To(TargetFormat target) => new DynamicTargetBuilder(Draft, target);

        /// <summary>Converts to ZPL. All labels are concatenated into one document.</summary>
        public ZplTargetBuilder ToZpl() => new ZplTargetBuilder(Draft);

        /// <summary>
        /// Converts to EPL. All labels are concatenated into one document. Read
        /// <see cref="ConversionResult.Bytes"/> rather than <see cref="ConversionResult.Text"/>:
        /// EPL's <c>GW</c> command inlines raw binary.
        /// </summary>
        public TargetBuilder ToEpl() => new TargetBuilder(Draft, TargetFormat.Epl);

        /// <summary>
        /// Converts to Intermec IPL. All labels are concatenated into one document. As with
        /// <see cref="ToEpl"/>, prefer <see cref="ConversionResult.Bytes"/>: commands are framed
        /// in <c>STX</c>/<c>ETX</c>.
        /// </summary>
        public TargetBuilder ToIpl() => new TargetBuilder(Draft, TargetFormat.Ipl);

        /// <summary>
        /// Converts to TSPL. All labels are concatenated into one document. As with
        /// <see cref="ToEpl"/>, prefer <see cref="ConversionResult.Bytes"/>.
        /// </summary>
        public TargetBuilder ToTspl() => new TargetBuilder(Draft, TargetFormat.Tspl);

        /// <summary>Converts to Datamax DPL. All labels are concatenated into one document.</summary>
        public TargetBuilder ToDpl() => new TargetBuilder(Draft, TargetFormat.Dpl);

        /// <summary>
        /// Converts to SATO SBPL. All labels are concatenated into one document. As with
        /// <see cref="ToEpl"/>, prefer <see cref="ConversionResult.Bytes"/>: every command is
        /// framed with <c>ESC</c>.
        /// </summary>
        public TargetBuilder ToSbpl() => new TargetBuilder(Draft, TargetFormat.Sbpl);

        /// <summary>Converts to LabelZoom XML. Returns the first label only.</summary>
        public TargetBuilder ToXml() => new TargetBuilder(Draft, TargetFormat.Xml);

        /// <summary>Converts to LabelZoom JSON. Returns the first label only; requires a paid license.</summary>
        public TargetBuilder ToJson() => new TargetBuilder(Draft, TargetFormat.Json);

        /// <summary>Converts to PDF, one page per label.</summary>
        public TargetBuilder ToPdf() => new TargetBuilder(Draft, TargetFormat.Pdf);

        /// <summary>Converts to a PNG image. Returns the first label only.</summary>
        public TargetBuilder ToPng() => new TargetBuilder(Draft, TargetFormat.Png);

        /// <summary>Converts to a BMP image. Returns the first label only.</summary>
        public TargetBuilder ToBmp() => new TargetBuilder(Draft, TargetFormat.Bmp);

        /// <summary>Converts to a GIF image. Returns the first label only.</summary>
        public TargetBuilder ToGif() => new TargetBuilder(Draft, TargetFormat.Gif);

        /// <summary>Converts to a JPEG image. Returns the first label only.</summary>
        public TargetBuilder ToJpeg() => new TargetBuilder(Draft, TargetFormat.Jpeg);
    }
}
