using System;

namespace LabelZoom.Sdk.Conversion
{
    /// <summary>
    /// The source stage for a format with no options of its own: EPL, IPL, TSPL, DPL, SBPL,
    /// LabelZoom XML and LabelZoom JSON.
    /// </summary>
    public sealed class SourceBuilder : SourceStage<SourceBuilder>
    {
        internal SourceBuilder(ConversionDraft draft)
            : base(draft)
        {
        }
    }

    /// <summary>The source stage for a ZPL document.</summary>
    public sealed class ZplSourceBuilder : SourceStage<ZplSourceBuilder>
    {
        internal ZplSourceBuilder(ConversionDraft draft)
            : base(draft)
        {
        }

        /// <summary>ZPL commands the parser should skip, for example <c>^PQ</c>.</summary>
        public ZplSourceBuilder WithCommandsToIgnore(params string[] commands)
        {
            ParameterWriters.ZplCommandsToIgnore(Parameters, commands);
            return this;
        }
    }

    /// <summary>The source stage for a PNG, BMP, GIF or JPEG image.</summary>
    public sealed class ImageSourceBuilder : SourceStage<ImageSourceBuilder>
    {
        internal ImageSourceBuilder(ConversionDraft draft)
            : base(draft)
        {
        }

        /// <summary>
        /// Cuts the image into a grid of labels, read left to right and then down. ZPL target only.
        /// </summary>
        /// <example><c>.WithSplit(s =&gt; s.Rows(2).Columns(2).SkipBlank())</c></example>
        public ImageSourceBuilder WithSplit(Action<SplitOptions> configure)
        {
            ParameterWriters.Split(Parameters, configure);
            return this;
        }
    }

    /// <summary>The source stage for a PDF document.</summary>
    public sealed class PdfSourceBuilder : SourceStage<PdfSourceBuilder>
    {
        internal PdfSourceBuilder(ConversionDraft draft)
            : base(draft)
        {
        }

        /// <summary>
        /// Converts a single page, identified by a <b>0-based</b> index. Omit this to convert
        /// every page.
        /// </summary>
        /// <param name="zeroBasedPageNumber">0 is the first page.</param>
        public PdfSourceBuilder WithPage(int zeroBasedPageNumber)
        {
            ParameterWriters.PdfPage(Parameters, zeroBasedPageNumber);
            return this;
        }

        /// <summary>
        /// How the PDF is read. The server default is <see cref="PdfConversionMode.Image"/>, which
        /// splitting and layout require.
        /// </summary>
        public PdfSourceBuilder WithConversionMode(PdfConversionMode mode)
        {
            ParameterWriters.PdfConversionMode(Parameters, mode);
            return this;
        }

        /// <summary>
        /// Cuts each page into a grid of labels, such as a Letter sheet of 30, read left to right
        /// and then down. ZPL target only.
        /// </summary>
        /// <example><c>.WithSplit(s =&gt; s.Rows(10).Columns(3).Margins(0.19f, 0.5f))</c></example>
        public PdfSourceBuilder WithSplit(Action<SplitOptions> configure)
        {
            ParameterWriters.Split(Parameters, configure);
            return this;
        }
    }

    /// <summary>
    /// The source stage for a format chosen at run time, through
    /// <see cref="ConversionRequestBuilder.From(SourceFormat, byte[])"/> and its overloads.
    /// </summary>
    /// <remarks>
    /// The compiler can't know the format here, so every source option is available, named with
    /// the format it applies to. The server ignores an option that doesn't apply to the format.
    /// </remarks>
    public sealed class DynamicSourceBuilder : SourceStage<DynamicSourceBuilder>
    {
        internal DynamicSourceBuilder(ConversionDraft draft)
            : base(draft)
        {
        }

        /// <inheritdoc cref="PdfSourceBuilder.WithPage"/>
        public DynamicSourceBuilder WithPdfPage(int zeroBasedPageNumber)
        {
            ParameterWriters.PdfPage(Parameters, zeroBasedPageNumber);
            return this;
        }

        /// <inheritdoc cref="PdfSourceBuilder.WithConversionMode"/>
        public DynamicSourceBuilder WithPdfConversionMode(PdfConversionMode mode)
        {
            ParameterWriters.PdfConversionMode(Parameters, mode);
            return this;
        }

        /// <inheritdoc cref="ZplSourceBuilder.WithCommandsToIgnore"/>
        public DynamicSourceBuilder WithZplCommandsToIgnore(params string[] commands)
        {
            ParameterWriters.ZplCommandsToIgnore(Parameters, commands);
            return this;
        }

        /// <inheritdoc cref="PdfSourceBuilder.WithSplit"/>
        public DynamicSourceBuilder WithSplit(Action<SplitOptions> configure)
        {
            ParameterWriters.Split(Parameters, configure);
            return this;
        }
    }
}
