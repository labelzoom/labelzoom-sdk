using System;
using LabelZoom.Sdk.Conversion;

namespace LabelZoom.Sdk.Printing
{
    // Each builder here is the print twin of the convert source builder with the same prefix, and offers
    // the same source options through the same ParameterWriters calls. StageShapeTests holds the sets equal.

    /// <summary>Prints LabelZoom XML or JSON. They record their own resolution, so there is no <c>WithDpi</c>.</summary>
    public sealed class PrintBuilder : PrintStage<PrintBuilder>
    {
        internal PrintBuilder(PrintDraft draft)
            : base(draft)
        {
        }
    }

    /// <summary>Prints EPL, IPL, TSPL, DPL, SBPL, Fingerprint or CPCL.</summary>
    public sealed class PrinterLanguagePrintBuilder : PrintStage<PrinterLanguagePrintBuilder>
    {
        internal PrinterLanguagePrintBuilder(PrintDraft draft)
            : base(draft)
        {
        }

        /// <summary>
        /// The resolution the document was made for, in dots per inch. The server default is 203. Set it
        /// when the document was written for a 300 or 600 dpi head.
        /// </summary>
        public PrinterLanguagePrintBuilder WithDpi(int dpi)
        {
            ParameterWriters.SourceDpi(Parameters, dpi);
            return this;
        }
    }

    /// <summary>Prints a ZPL document.</summary>
    public sealed class ZplPrintBuilder : PrintStage<ZplPrintBuilder>
    {
        internal ZplPrintBuilder(PrintDraft draft)
            : base(draft)
        {
        }

        /// <summary>
        /// The resolution the document was made for, in dots per inch. The server default is 203. Set it
        /// when the document was written for a 300 or 600 dpi head.
        /// </summary>
        public ZplPrintBuilder WithDpi(int dpi)
        {
            ParameterWriters.SourceDpi(Parameters, dpi);
            return this;
        }

        /// <summary>ZPL commands to skip while reading the document, such as <c>^PQ</c>.</summary>
        public ZplPrintBuilder WithCommandsToIgnore(params string[] commands)
        {
            ParameterWriters.ZplCommandsToIgnore(Parameters, commands);
            return this;
        }
    }

    /// <summary>Prints a PNG, BMP, GIF or JPEG image.</summary>
    public sealed class ImagePrintBuilder : PrintStage<ImagePrintBuilder>
    {
        internal ImagePrintBuilder(PrintDraft draft)
            : base(draft)
        {
        }

        /// <summary>The image's resolution, in dots per inch: how its pixels map to inches.</summary>
        public ImagePrintBuilder WithDpi(int dpi)
        {
            ParameterWriters.SourceDpi(Parameters, dpi);
            return this;
        }

        /// <summary>Cuts each image into a grid of labels. See <see cref="SplitOptions"/>.</summary>
        public ImagePrintBuilder WithSplit(Action<SplitOptions> configure)
        {
            ParameterWriters.Split(Parameters, configure);
            return this;
        }

        /// <summary>How the image is scaled, placed and reduced to black and white. See <see cref="RasterOptions"/>.</summary>
        public ImagePrintBuilder WithRaster(Action<RasterOptions> configure)
        {
            ParameterWriters.Raster(Parameters, configure);
            return this;
        }
    }

    /// <summary>Prints a PDF document.</summary>
    public sealed class PdfPrintBuilder : PrintStage<PdfPrintBuilder>
    {
        internal PdfPrintBuilder(PrintDraft draft)
            : base(draft)
        {
        }

        /// <summary>Prints only this <b>0-based</b> page.</summary>
        public PdfPrintBuilder WithPage(int zeroBasedPageNumber)
        {
            ParameterWriters.PdfPage(Parameters, zeroBasedPageNumber);
            return this;
        }

        /// <summary>Whether the PDF is rasterized or read as text and vector shapes.</summary>
        public PdfPrintBuilder WithConversionMode(PdfConversionMode mode)
        {
            ParameterWriters.PdfConversionMode(Parameters, mode);
            return this;
        }

        /// <summary>Cuts each page into a grid of labels. See <see cref="SplitOptions"/>.</summary>
        public PdfPrintBuilder WithSplit(Action<SplitOptions> configure)
        {
            ParameterWriters.Split(Parameters, configure);
            return this;
        }

        /// <summary>How each page is scaled, placed and reduced to black and white. See <see cref="RasterOptions"/>.</summary>
        public PdfPrintBuilder WithRaster(Action<RasterOptions> configure)
        {
            ParameterWriters.Raster(Parameters, configure);
            return this;
        }
    }

    /// <summary>
    /// Prints a document whose format is chosen at run time. Every source option is available, named for
    /// the format it applies to; the server ignores one that doesn't apply.
    /// </summary>
    public sealed class DynamicPrintBuilder : PrintStage<DynamicPrintBuilder>
    {
        internal DynamicPrintBuilder(PrintDraft draft)
            : base(draft)
        {
        }

        /// <summary>The source resolution, in dots per inch, for a printer language or an image.</summary>
        public DynamicPrintBuilder WithDpi(int dpi)
        {
            ParameterWriters.SourceDpi(Parameters, dpi);
            return this;
        }

        /// <summary>For a PDF: prints only this <b>0-based</b> page.</summary>
        public DynamicPrintBuilder WithPdfPage(int zeroBasedPageNumber)
        {
            ParameterWriters.PdfPage(Parameters, zeroBasedPageNumber);
            return this;
        }

        /// <summary>For a PDF: whether it is rasterized or read as text and vector shapes.</summary>
        public DynamicPrintBuilder WithPdfConversionMode(PdfConversionMode mode)
        {
            ParameterWriters.PdfConversionMode(Parameters, mode);
            return this;
        }

        /// <summary>For ZPL: commands to skip while reading the document.</summary>
        public DynamicPrintBuilder WithZplCommandsToIgnore(params string[] commands)
        {
            ParameterWriters.ZplCommandsToIgnore(Parameters, commands);
            return this;
        }

        /// <summary>For a PDF or an image: cuts each page into a grid of labels.</summary>
        public DynamicPrintBuilder WithSplit(Action<SplitOptions> configure)
        {
            ParameterWriters.Split(Parameters, configure);
            return this;
        }

        /// <summary>For a PDF or an image: how it is scaled, placed and reduced to black and white.</summary>
        public DynamicPrintBuilder WithRaster(Action<RasterOptions> configure)
        {
            ParameterWriters.Raster(Parameters, configure);
            return this;
        }
    }
}
