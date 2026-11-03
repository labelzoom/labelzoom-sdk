namespace LabelZoom.Sdk.Conversion
{
    /// <summary>
    /// Cuts each page of an image or PDF source into a grid of labels, read left to right and then
    /// down. Configured inside <c>WithSplit(s =&gt; …)</c>.
    /// </summary>
    /// <remarks>
    /// Applies to image or PDF → ZPL in <see cref="PdfConversionMode.Image"/> mode. Every length is
    /// in <b>inches</b> on the page as a viewer displays it, measured from its top-left corner. From
    /// the cut onward, every other option sees labels rather than pages: the label size, rotation
    /// and position apply to each label, and the label index counts labels.
    /// </remarks>
    public sealed class SplitOptions
    {
        private readonly ConversionParameters _parameters;

        internal SplitOptions(ConversionParameters parameters) => _parameters = parameters;

        /// <summary>Rows of labels on each page. The server default is 1.</summary>
        public SplitOptions Rows(int rows) => Set("rows", rows);

        /// <summary>Columns of labels on each page. The server default is 1.</summary>
        public SplitOptions Columns(int columns) => Set("columns", columns);

        /// <summary>
        /// The size of one label on the page, <b>in inches</b>. Omit this to divide the page equally.
        /// </summary>
        public SplitOptions LabelSize(float widthInches, float heightInches) =>
            Set("width", widthInches).Set("height", heightInches);

        /// <summary>Distance from the page's left and top edges to the first label, <b>in inches</b>.</summary>
        public SplitOptions Margins(float leftInches, float topInches) =>
            Set("left", leftInches).Set("top", topInches);

        /// <summary>Space between two columns and between two rows of labels, <b>in inches</b>.</summary>
        public SplitOptions Gaps(float columnGapInches, float rowGapInches) =>
            Set("columnGap", columnGapInches).Set("rowGap", rowGapInches);

        /// <summary>
        /// Leaves out labels with nothing printable, such as the unused end of a partly filled sheet.
        /// </summary>
        public SplitOptions SkipBlank(bool skipBlank = true) => Set("skipBlank", skipBlank);

        // Merged into the one "split" object, never replacing it: contract §4.1.
        private SplitOptions Set(string key, object value)
        {
            _parameters.SetNested("split", key, value);
            return this;
        }
    }
}
