using System.Threading;
using System.Threading.Tasks;

namespace LabelZoom.Sdk.Conversion
{
    /// <summary>
    /// The target stage: options that change the <b>output</b>, followed by
    /// <see cref="ExecuteAsync"/>.
    /// </summary>
    /// <remarks>
    /// The options here apply to every target. ZPL-specific ones live on
    /// <see cref="ZplTargetBuilder"/>. An option the server doesn't apply to the chosen format pair
    /// is ignored rather than rejected.
    /// </remarks>
    /// <typeparam name="TSelf">The concrete builder, so each method returns it unchanged.</typeparam>
    public abstract class TargetStage<TSelf>
        where TSelf : TargetStage<TSelf>
    {
        private readonly ConversionDraft _draft;
        private readonly TargetFormat _target;

        private protected TargetStage(ConversionDraft draft, TargetFormat target)
        {
            _draft = draft;
            _target = target;
        }

        private protected ConversionParameters Parameters => _draft.Parameters;

        private protected TSelf Self => (TSelf)this;

        /// <summary>Output resolution in dots per inch. The server default is 203.</summary>
        public TSelf WithDpi(int dpi)
        {
            ParameterWriters.Dpi(Parameters, dpi);
            return Self;
        }

        /// <summary>Rotation in degrees. Must be a multiple of 90. The server default is 0.</summary>
        /// <param name="rotation">Degrees clockwise: 0, 90, 180 or 270.</param>
        public TSelf WithRotation(int rotation)
        {
            ParameterWriters.Rotation(Parameters, rotation);
            return Self;
        }

        /// <summary>Scaling as a percentage. The server default is 100.</summary>
        public TSelf WithScaling(float percent)
        {
            ParameterWriters.Scaling(Parameters, percent);
            return Self;
        }

        /// <summary>Color handling. The server default is <see cref="ColorMode.Grayscale"/>.</summary>
        public TSelf WithColorMode(ColorMode mode)
        {
            Parameters.Set("colorMode", mode.ToWireToken());
            return Self;
        }

        /// <summary>
        /// Luminance threshold from 0 to 100 used when reducing color depth. The server default is 70.
        /// </summary>
        public TSelf WithDarkness(int darkness)
        {
            ParameterWriters.Darkness(Parameters, darkness);
            return Self;
        }

        /// <summary>Pixel offset of the top-left corner of the extracted region.</summary>
        public TSelf WithPosition(int x, int y)
        {
            ParameterWriters.Position(Parameters, x, y);
            return Self;
        }

        /// <summary>
        /// Requests a watermark. Output is watermarked regardless on the anonymous free tier.
        /// </summary>
        public TSelf WithWatermark(bool watermark = true)
        {
            Parameters.Set("watermark", watermark);
            return Self;
        }

        /// <summary>
        /// Selects a printer dialect, for example <c>moca</c> for Blue Yonder WMS. Requires a paid
        /// license; without one the request fails with a 403 whose
        /// <see cref="LabelZoomForbiddenException.IsPaidFeature"/> is set.
        /// </summary>
        public TSelf WithDialect(string dialect)
        {
            ParameterWriters.Dialect(Parameters, dialect);
            return Self;
        }

        /// <summary>
        /// Sets a parameter the SDK does not model yet. Unknown keys are ignored by the server, so
        /// this is a safe forward-compatibility escape hatch.
        /// </summary>
        public TSelf WithParameter(string key, object? value)
        {
            ParameterWriters.Parameter(Parameters, key, value);
            return Self;
        }

        /// <summary>
        /// Adds a raw query-string parameter alongside <c>params</c>. For endpoints or options that
        /// are not expressible as conversion parameters.
        /// </summary>
        public TSelf WithRawQueryParameter(string key, string value)
        {
            ParameterWriters.RawQueryParameter(Parameters, key, value);
            return Self;
        }

        /// <summary>Executes the conversion.</summary>
        /// <returns>
        /// The converted document. Use <see cref="ConversionResult.Text"/> for ZPL, XML and JSON
        /// targets, and <see cref="ConversionResult.Bytes"/> for PDF, images and the other printer
        /// languages.
        /// </returns>
        /// <exception cref="LabelZoomException">The API returned a non-2xx response.</exception>
        /// <remarks>
        /// The options are copied when this is called, so a builder can be executed again, or
        /// changed and executed again, without affecting a request already in flight.
        /// </remarks>
        public Task<ConversionResult> ExecuteAsync(CancellationToken cancellationToken = default) =>
            _draft.Client.ExecuteAsync(
                _draft.Source,
                _target,
                _draft.Body,
                _draft.ContentType,
                _draft.Parameters.Clone(),
                cancellationToken);
    }
}
