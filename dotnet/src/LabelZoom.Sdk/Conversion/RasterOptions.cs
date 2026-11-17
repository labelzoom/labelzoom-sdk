namespace LabelZoom.Sdk.Conversion
{
    /// <summary>
    /// How an image or PDF source is rasterized before it becomes labels. Configured inside
    /// <c>WithRaster(r =&gt; …)</c>.
    /// </summary>
    /// <remarks>
    /// These act on the source picture, so they belong with <c>WithSplit</c> and the label size, which
    /// cut and frame the same picture. Rotation is the exception: it turns the finished label for every
    /// format, so it is set on the target stage.
    /// </remarks>
    public sealed class RasterOptions
    {
        private readonly ConversionParameters _parameters;

        internal RasterOptions(ConversionParameters parameters) => _parameters = parameters;

        /// <summary>
        /// Scales the picture, as a percentage. The server default is 100. A PDF is rendered larger or
        /// smaller; an image is resampled.
        /// </summary>
        public RasterOptions Scaling(float percent)
        {
            ParameterWriters.Scaling(_parameters, percent);
            return this;
        }

        /// <summary>
        /// Where the picture sits on its label, in output dots, measured from the label's top-left corner.
        /// A positive offset moves it right or down; a negative one crops that much off its left or top. With
        /// a label size, this is how a picture is cropped. Printer-language targets only.
        /// </summary>
        public RasterOptions Position(int x, int y)
        {
            ParameterWriters.Position(_parameters, x, y);
            return this;
        }

        /// <summary>
        /// Luminance threshold from 0 to 100 for reducing the picture to black and white. The server default
        /// is 70. It also decides which labels <c>SplitOptions.SkipBlank</c> treats as blank.
        /// </summary>
        public RasterOptions Darkness(int darkness)
        {
            ParameterWriters.Darkness(_parameters, darkness);
            return this;
        }

        /// <summary>
        /// How the picture's color is reduced. The server default is <see cref="Sdk.ColorMode.Grayscale"/>,
        /// which dithers it.
        /// </summary>
        public RasterOptions ColorMode(ColorMode mode)
        {
            ParameterWriters.ColorMode(_parameters, mode);
            return this;
        }
    }
}
