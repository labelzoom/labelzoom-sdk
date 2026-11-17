namespace LabelZoom.Sdk.Conversion
{
    /// <summary>
    /// Puts labels side by side in each printed row, for multi-column roll stock. Configured inside
    /// <c>WithLayout(l =&gt; …)</c>.
    /// </summary>
    /// <remarks>
    /// Applies to image or PDF → ZPL in <see cref="PdfConversionMode.Image"/> mode. The response's
    /// <c>^PW</c> is the width of the whole row, so the label size stays the size of <b>one</b>
    /// label, not the roll.
    /// </remarks>
    public sealed class LayoutOptions
    {
        private readonly ConversionParameters _parameters;

        internal LayoutOptions(ConversionParameters parameters) => _parameters = parameters;

        /// <summary>Labels across the roll, 1 to 10. The server default is 1.</summary>
        public LayoutOptions Across(int across) => Set("across", across);

        /// <summary>Space between two labels in a row, <b>in inches</b>. The server default is 0.</summary>
        public LayoutOptions Gap(float gapInches) => Set("gap", gapInches);

        /// <summary>
        /// How labels fill the columns of a row. The server default is
        /// <see cref="LayoutFill.Sequential"/>; use <see cref="LayoutFill.Repeat"/> for a single
        /// image or a one-page file, where sequential fill would leave every column after the first
        /// blank.
        /// </summary>
        public LayoutOptions Fill(LayoutFill fill) => Set("fill", fill.ToWireToken());

        // Merged into the one "layout" object, never replacing it: contract §4.1.
        private LayoutOptions Set(string key, object value)
        {
            _parameters.SetNested("layout", key, value);
            return this;
        }
    }
}
