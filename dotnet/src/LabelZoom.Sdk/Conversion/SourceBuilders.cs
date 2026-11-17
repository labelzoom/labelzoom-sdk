using System;

namespace LabelZoom.Sdk.Conversion
{
    /// <summary>
    /// The source stage for LabelZoom XML and LabelZoom JSON. They record their own resolution, so there is
    /// no <c>WithDpi</c>.
    /// </summary>
    public sealed class SourceBuilder : SourceStage<SourceBuilder>
    {
        internal SourceBuilder(ConversionDraft draft)
            : base(draft)
        {
        }
    }

    /// <summary>The source stage for EPL, IPL, TSPL, DPL and SBPL.</summary>
    public sealed class PrinterLanguageSourceBuilder : SourceStage<PrinterLanguageSourceBuilder>
    {
        internal PrinterLanguageSourceBuilder(ConversionDraft draft)
            : base(draft)
        {
        }

        /// <summary>
        /// The resolution the source was made for, in dots per inch: how its dots or pixels map to inches.
        /// The server default is 203. The output follows it unless the target stage sets its own.
        /// </summary>
        public PrinterLanguageSourceBuilder WithDpi(int dpi)
        {
            ParameterWriters.SourceDpi(Parameters, dpi);
            return this;
        }
    }

    /// <summary>The source stage for a ZPL document.</summary>
    public sealed class ZplSourceBuilder : SourceStage<ZplSourceBuilder>
    {
        internal ZplSourceBuilder(ConversionDraft draft)
            : base(draft)
        {
        }

        /// <summary>
        /// The resolution the source was made for, in dots per inch: how its dots or pixels map to inches.
        /// The server default is 203. The output follows it unless the target stage sets its own.
        /// </summary>
        public ZplSourceBuilder WithDpi(int dpi)
        {
            ParameterWriters.SourceDpi(Parameters, dpi);
            return this;
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
        /// The resolution the source was made for, in dots per inch: how its dots or pixels map to inches.
        /// The server default is 203. The output follows it unless the target stage sets its own.
        /// </summary>
        public ImageSourceBuilder WithDpi(int dpi)
        {
            ParameterWriters.SourceDpi(Parameters, dpi);
            return this;
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

        /// <summary>
        /// How the image is rasterized: scaling, its position on the label (which, with a label size, crops
        /// it), the black-and-white threshold and the color reduction.
        /// </summary>
        /// <example><c>.WithRaster(r =&gt; r.Scaling(150).Position(-20, 0).Darkness(60))</c></example>
        public ImageSourceBuilder WithRaster(Action<RasterOptions> configure)
        {
            ParameterWriters.Raster(Parameters, configure);
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

        /// <summary>
        /// How each page is rasterized: scaling, its position on the label (which, with a label size, crops
        /// it), the black-and-white threshold and the color reduction. In
        /// <see cref="PdfConversionMode.Native"/> mode the page isn't rasterized, and only scaling applies.
        /// </summary>
        /// <example><c>.WithRaster(r =&gt; r.Scaling(150).Darkness(60).ColorMode(ColorMode.Grayscale))</c></example>
        public PdfSourceBuilder WithRaster(Action<RasterOptions> configure)
        {
            ParameterWriters.Raster(Parameters, configure);
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

        /// <summary>
        /// The resolution the source was made for, in dots per inch: how its dots or pixels map to inches.
        /// The server default is 203. The output follows it unless the target stage sets its own.
        /// </summary>
        public DynamicSourceBuilder WithDpi(int dpi)
        {
            ParameterWriters.SourceDpi(Parameters, dpi);
            return this;
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

        /// <inheritdoc cref="PdfSourceBuilder.WithRaster"/>
        public DynamicSourceBuilder WithRaster(Action<RasterOptions> configure)
        {
            ParameterWriters.Raster(Parameters, configure);
            return this;
        }
    }
}
