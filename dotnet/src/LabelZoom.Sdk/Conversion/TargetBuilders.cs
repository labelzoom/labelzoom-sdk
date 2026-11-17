using System;

namespace LabelZoom.Sdk.Conversion
{
    /// <summary>
    /// The target stage for EPL, IPL, TSPL, DPL, SBPL, CPCL, LabelZoom XML and LabelZoom JSON.
    /// </summary>
    public sealed class TargetBuilder : TargetStage<TargetBuilder>
    {
        internal TargetBuilder(ConversionDraft draft, TargetFormat target)
            : base(draft, target)
        {
        }

        /// <summary>
        /// The resolution of the output, in dots per inch. The server default is the source's resolution,
        /// else 203. To move a label to a printer of another density at the same physical size, set the
        /// source's resolution too.
        /// </summary>
        public TargetBuilder WithDpi(int dpi)
        {
            ParameterWriters.TargetDpi(Parameters, dpi);
            return this;
        }
    }

    /// <summary>
    /// The target stage for PDF. A PDF has no resolution of its own, so there is no <c>WithDpi</c>: the
    /// label keeps its physical size.
    /// </summary>
    public sealed class PdfTargetBuilder : TargetStage<PdfTargetBuilder>
    {
        internal PdfTargetBuilder(ConversionDraft draft)
            : base(draft, TargetFormat.Pdf)
        {
        }
    }

    /// <summary>The target stage for a PNG, BMP, GIF or JPEG image.</summary>
    public sealed class ImageTargetBuilder : TargetStage<ImageTargetBuilder>
    {
        internal ImageTargetBuilder(ConversionDraft draft, TargetFormat target)
            : base(draft, target)
        {
        }

        /// <summary>
        /// The resolution of the output, in dots per inch. The server default is the source's resolution,
        /// else 203. To move a label to a printer of another density at the same physical size, set the
        /// source's resolution too.
        /// </summary>
        public ImageTargetBuilder WithDpi(int dpi)
        {
            ParameterWriters.TargetDpi(Parameters, dpi);
            return this;
        }

        /// <summary>
        /// <see cref="ColorMode.Bw"/> renders a black-and-white image at the <see cref="WithDarkness"/>
        /// threshold. Any other mode renders in color, which is the default.
        /// </summary>
        public ImageTargetBuilder WithColorMode(ColorMode mode)
        {
            ParameterWriters.ColorMode(Parameters, mode);
            return this;
        }

        /// <summary>
        /// Luminance threshold from 0 to 100 for a black-and-white image. The server default is 70.
        /// </summary>
        public ImageTargetBuilder WithDarkness(int darkness)
        {
            ParameterWriters.Darkness(Parameters, darkness);
            return this;
        }
    }

    /// <summary>The target stage for ZPL output.</summary>
    public sealed class ZplTargetBuilder : TargetStage<ZplTargetBuilder>
    {
        internal ZplTargetBuilder(ConversionDraft draft)
            : base(draft, TargetFormat.Zpl)
        {
        }

        /// <summary>
        /// The resolution of the output, in dots per inch. The server default is the source's resolution,
        /// else 203. To move a label to a printer of another density at the same physical size, set the
        /// source's resolution too.
        /// </summary>
        public ZplTargetBuilder WithDpi(int dpi)
        {
            ParameterWriters.TargetDpi(Parameters, dpi);
            return this;
        }

        /// <summary>
        /// Image compression used in the ZPL output. The server default is
        /// <see cref="ZplImageCompression.Z64"/>.
        /// </summary>
        public ZplTargetBuilder WithImageCompression(ZplImageCompression compression)
        {
            ParameterWriters.ZplImageCompression(Parameters, compression);
            return this;
        }

        /// <summary>
        /// Image or PDF source: sends each run of identical labels as one label with a <c>^PQ</c>
        /// print quantity. The server default is <c>false</c>, because this changes the number of
        /// labels in the output.
        /// </summary>
        public ZplTargetBuilder WithCollapseCopies(bool collapse = true)
        {
            ParameterWriters.ZplCollapseCopies(Parameters, collapse);
            return this;
        }

        /// <summary>
        /// Image or PDF source: puts labels side by side in each printed row, for multi-column
        /// roll stock.
        /// </summary>
        /// <example><c>.WithLayout(l =&gt; l.Across(2).Gap(0.125f))</c></example>
        public ZplTargetBuilder WithLayout(Action<LayoutOptions> configure)
        {
            ParameterWriters.Layout(Parameters, configure);
            return this;
        }
    }

    /// <summary>
    /// The target stage for a format chosen at run time, through
    /// <see cref="SourceStage{TSelf}.To(TargetFormat)"/>.
    /// </summary>
    /// <remarks>
    /// The compiler can't know the format here, so every target option is available, named with
    /// the format it applies to. The server ignores an option that doesn't apply to the format.
    /// </remarks>
    public sealed class DynamicTargetBuilder : TargetStage<DynamicTargetBuilder>
    {
        internal DynamicTargetBuilder(ConversionDraft draft, TargetFormat target)
            : base(draft, target)
        {
        }

        /// <summary>
        /// The resolution of the output, in dots per inch. The server default is the source's resolution,
        /// else 203. To move a label to a printer of another density at the same physical size, set the
        /// source's resolution too.
        /// </summary>
        public DynamicTargetBuilder WithDpi(int dpi)
        {
            ParameterWriters.TargetDpi(Parameters, dpi);
            return this;
        }

        /// <inheritdoc cref="ZplTargetBuilder.WithImageCompression"/>
        public DynamicTargetBuilder WithZplImageCompression(ZplImageCompression compression)
        {
            ParameterWriters.ZplImageCompression(Parameters, compression);
            return this;
        }

        /// <inheritdoc cref="ZplTargetBuilder.WithCollapseCopies"/>
        public DynamicTargetBuilder WithZplCollapseCopies(bool collapse = true)
        {
            ParameterWriters.ZplCollapseCopies(Parameters, collapse);
            return this;
        }

        /// <inheritdoc cref="ZplTargetBuilder.WithLayout"/>
        public DynamicTargetBuilder WithLayout(Action<LayoutOptions> configure)
        {
            ParameterWriters.Layout(Parameters, configure);
            return this;
        }

        /// <inheritdoc cref="ImageTargetBuilder.WithColorMode"/>
        /// <remarks>
        /// This writes the same <c>colorMode</c> as <see cref="RasterOptions.ColorMode"/> on the source
        /// stage, so set it once: the last call wins.
        /// </remarks>
        public DynamicTargetBuilder WithColorMode(ColorMode mode)
        {
            ParameterWriters.ColorMode(Parameters, mode);
            return this;
        }

        /// <inheritdoc cref="ImageTargetBuilder.WithDarkness"/>
        /// <remarks>As with <see cref="WithColorMode"/>, the source stage writes the same <c>darkness</c>.</remarks>
        public DynamicTargetBuilder WithDarkness(int darkness)
        {
            ParameterWriters.Darkness(Parameters, darkness);
            return this;
        }
    }
}
