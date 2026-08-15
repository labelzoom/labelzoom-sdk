using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LabelZoom.Sdk.Conversion
{
    /// <summary>
    /// Configures and executes a conversion.
    /// </summary>
    /// <remarks>
    /// Every <c>With*</c> method records its value and is actually sent. Options set here that the
    /// server does not apply to the chosen format pair are ignored server-side rather than
    /// rejected, so combining them is safe.
    /// </remarks>
    public sealed class ConversionTargetBuilder
    {
        private readonly LabelZoomClient _client;
        private readonly SourceFormat _source;
        private readonly TargetFormat _target;
        private readonly byte[] _body;
        private readonly string _contentType;
        private readonly ConversionParameters _parameters = new ConversionParameters();

        internal ConversionTargetBuilder(
            LabelZoomClient client,
            SourceFormat source,
            TargetFormat target,
            byte[] body,
            string contentType)
        {
            _client = client;
            _source = source;
            _target = target;
            _body = body;
            _contentType = contentType;
        }

        /// <summary>Output resolution in dots per inch. The server default is 203.</summary>
        public ConversionTargetBuilder WithDpi(int dpi)
        {
            if (dpi <= 0)
            {
                throw new LabelZoomValidationException(nameof(dpi), "DPI must be greater than zero.");
            }

            _parameters.Set("dpi", dpi);
            return this;
        }

        /// <summary>Rotation in degrees. Must be a multiple of 90. The server default is 0.</summary>
        /// <param name="rotation">Degrees clockwise: 0, 90, 180 or 270.</param>
        public ConversionTargetBuilder WithRotation(int rotation)
        {
            // Rejected locally: the server would 400, and this is unambiguously a caller bug.
            if (rotation % 90 != 0)
            {
                throw new LabelZoomValidationException(
                    nameof(rotation), $"Rotation must be a multiple of 90 degrees, but was {rotation}.");
            }

            _parameters.Set("rotation", rotation);
            return this;
        }

        /// <summary>Scaling as a percentage. The server default is 100.</summary>
        public ConversionTargetBuilder WithScaling(float percent)
        {
            if (percent <= 0)
            {
                throw new LabelZoomValidationException(nameof(percent), "Scaling must be greater than zero.");
            }

            _parameters.Set("scaling", percent);
            return this;
        }

        /// <summary>Colour handling. The server default is <see cref="ColorMode.Grayscale"/>.</summary>
        public ConversionTargetBuilder WithColorMode(ColorMode mode)
        {
            _parameters.Set("colorMode", mode.ToWireToken());
            return this;
        }

        /// <summary>
        /// Luminance threshold from 0 to 100 used when reducing colour depth. The server default is 70.
        /// </summary>
        public ConversionTargetBuilder WithDarkness(int darkness)
        {
            if (darkness < 0 || darkness > 100)
            {
                throw new LabelZoomValidationException(
                    nameof(darkness), $"Darkness must be between 0 and 100, but was {darkness}.");
            }

            _parameters.Set("darkness", darkness);
            return this;
        }

        /// <summary>Pixel offset of the top-left corner of the extracted region.</summary>
        public ConversionTargetBuilder WithPosition(int x, int y)
        {
            _parameters.SetNested("position", "x", x);
            _parameters.SetNested("position", "y", y);
            return this;
        }

        /// <summary>
        /// Requests a watermark. Output is watermarked regardless on the anonymous free tier.
        /// </summary>
        public ConversionTargetBuilder WithWatermark(bool watermark = true)
        {
            _parameters.Set("watermark", watermark);
            return this;
        }

        /// <summary>
        /// Selects a printer dialect, for example <c>moca</c> for Blue Yonder WMS. Requires a paid
        /// license; without one the request fails with a 403 whose
        /// <see cref="LabelZoomForbiddenException.IsPaidFeature"/> is set.
        /// </summary>
        public ConversionTargetBuilder WithDialect(string dialect)
        {
            if (string.IsNullOrWhiteSpace(dialect))
            {
                throw new LabelZoomValidationException(nameof(dialect), "Dialect cannot be null or empty.");
            }

            _parameters.Set("dialect", dialect);
            return this;
        }

        /// <summary>
        /// Label dimensions <b>in inches</b>, overriding whatever the source document implies.
        /// </summary>
        /// <param name="widthInches">Width in inches — not dots, not millimetres.</param>
        /// <param name="heightInches">Height in inches.</param>
        public ConversionTargetBuilder WithLabelSize(float widthInches, float heightInches)
        {
            if (widthInches <= 0 || heightInches <= 0)
            {
                throw new LabelZoomValidationException(
                    nameof(widthInches), "Label width and height must be greater than zero.");
            }

            _parameters.SetNested("label", "width", widthInches);
            _parameters.SetNested("label", "height", heightInches);
            return this;
        }

        /// <summary>How a source PDF is interpreted. The server default is <see cref="PdfConversionMode.Image"/>.</summary>
        public ConversionTargetBuilder WithPdfConversionMode(PdfConversionMode mode)
        {
            _parameters.SetNested("pdf", "conversionMode", mode.ToWireToken());
            return this;
        }

        /// <summary>
        /// Converts a single page of a source PDF, identified by a <b>0-based</b> index. Omit this
        /// to convert every page.
        /// </summary>
        /// <param name="zeroBasedPageNumber">0 is the first page.</param>
        public ConversionTargetBuilder WithPdfPage(int zeroBasedPageNumber)
        {
            if (zeroBasedPageNumber < 0)
            {
                throw new LabelZoomValidationException(
                    nameof(zeroBasedPageNumber),
                    "Page number is 0-based and cannot be negative; 0 selects the first page.");
            }

            _parameters.SetNested("pdf", "pageNumber", zeroBasedPageNumber);
            return this;
        }

        /// <summary>ZPL commands the parser should skip, for example <c>^PQ</c>.</summary>
        public ConversionTargetBuilder WithZplCommandsToIgnore(params string[] commands)
        {
            if (commands is null || commands.Length == 0)
            {
                throw new LabelZoomValidationException(
                    nameof(commands), "Provide at least one command, or omit this call entirely.");
            }

            _parameters.SetNested("zpl", "commandsToIgnore", commands);
            return this;
        }

        /// <summary>
        /// Image compression used when writing ZPL. The server default is
        /// <see cref="ZplImageCompression.Z64"/>.
        /// </summary>
        public ConversionTargetBuilder WithZplImageCompression(ZplImageCompression compression)
        {
            _parameters.SetNested("zpl", "imageCompression", compression.ToWireToken());
            return this;
        }

        /// <summary>
        /// Supplies data to fill the label's variable fields. <b>Each record produces one label.</b>
        /// </summary>
        /// <param name="records">
        /// One or more objects whose properties are the label's variable field names. Anonymous
        /// types and dictionaries both work. A single record means a single label.
        /// </param>
        public ConversionTargetBuilder WithData(params object?[] records)
        {
            if (records is null || records.Length == 0)
            {
                throw new LabelZoomValidationException(
                    nameof(records), "Provide at least one data record, or omit this call entirely.");
            }

            _parameters.Set("data", ConversionParameters.NormalizeData(records));
            return this;
        }

        /// <summary>
        /// Sets a parameter the SDK does not model yet. Unknown keys are ignored by the server, so
        /// this is a safe forward-compatibility escape hatch.
        /// </summary>
        public ConversionTargetBuilder WithParameter(string key, object? value)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new LabelZoomValidationException(nameof(key), "Parameter key cannot be null or empty.");
            }

            _parameters.Set(key, value);
            return this;
        }

        /// <summary>
        /// Adds a raw query-string parameter alongside <c>params</c>. For endpoints or options that
        /// are not expressible as conversion parameters.
        /// </summary>
        public ConversionTargetBuilder WithRawQueryParameter(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new LabelZoomValidationException(nameof(key), "Query parameter key cannot be null or empty.");
            }

            _parameters.SetRawQueryParameter(key, value ?? string.Empty);
            return this;
        }

        /// <summary>Executes the conversion.</summary>
        /// <returns>
        /// The converted document. Use <see cref="ConversionResult.Text"/> for ZPL, XML and JSON
        /// targets, and <see cref="ConversionResult.Bytes"/> for PDF and images.
        /// </returns>
        /// <exception cref="LabelZoomException">The API returned a non-2xx response.</exception>
        public Task<ConversionResult> ExecuteAsync(CancellationToken cancellationToken = default) =>
            _client.ExecuteAsync(
                _source, _target, _body, _contentType, _parameters.Clone(), cancellationToken);
    }
}
