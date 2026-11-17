using System;

namespace LabelZoom.Sdk.Conversion
{
    /// <summary>
    /// The target stage for a format with no options of its own: every target except ZPL.
    /// </summary>
    public sealed class TargetBuilder : TargetStage<TargetBuilder>
    {
        internal TargetBuilder(ConversionDraft draft, TargetFormat target)
            : base(draft, target)
        {
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
    }
}
