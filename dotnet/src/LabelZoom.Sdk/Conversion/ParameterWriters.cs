using System;

namespace LabelZoom.Sdk.Conversion
{
    /// <summary>
    /// The validation and wire mapping for every option, written once.
    /// </summary>
    /// <remarks>
    /// Several builders expose the same option: <see cref="PdfSourceBuilder.WithPage"/> and
    /// <see cref="DynamicSourceBuilder.WithPdfPage"/> both write <c>pdf.pageNumber</c>. Each public
    /// method is a one-line call into here, so the builders cannot drift apart on validation or on
    /// the JSON path.
    /// </remarks>
    internal static class ParameterWriters
    {
        internal static void LabelSize(ConversionParameters p, float widthInches, float heightInches)
        {
            if (widthInches <= 0 || heightInches <= 0)
            {
                throw new LabelZoomValidationException(
                    nameof(widthInches), "Label width and height must be greater than zero.");
            }

            p.SetNested("label", "width", widthInches);
            p.SetNested("label", "height", heightInches);
        }

        // No local range check: contract §5 leaves labelIndex limits to the server.
        internal static void LabelIndex(ConversionParameters p, int zeroBasedIndex) =>
            p.Set("labelIndex", zeroBasedIndex);

        internal static void Data(ConversionParameters p, object?[] records)
        {
            if (records is null || records.Length == 0)
            {
                throw new LabelZoomValidationException(
                    nameof(records), "Provide at least one data record, or omit this call entirely.");
            }

            p.Set("data", ConversionParameters.NormalizeData(records));
        }

        internal static void PdfPage(ConversionParameters p, int zeroBasedPageNumber)
        {
            if (zeroBasedPageNumber < 0)
            {
                throw new LabelZoomValidationException(
                    nameof(zeroBasedPageNumber),
                    "Page number is 0-based and cannot be negative; 0 selects the first page.");
            }

            p.SetNested("pdf", "pageNumber", zeroBasedPageNumber);
        }

        internal static void PdfConversionMode(ConversionParameters p, PdfConversionMode mode) =>
            p.SetNested("pdf", "conversionMode", mode.ToWireToken());

        internal static void ZplCommandsToIgnore(ConversionParameters p, string[] commands)
        {
            if (commands is null || commands.Length == 0)
            {
                throw new LabelZoomValidationException(
                    nameof(commands), "Provide at least one command, or omit this call entirely.");
            }

            p.SetNested("zpl", "commandsToIgnore", commands);
        }

        internal static void Split(ConversionParameters p, Action<SplitOptions> configure)
        {
            if (configure is null)
            {
                throw new LabelZoomValidationException(nameof(configure), "Split configuration cannot be null.");
            }

            configure(new SplitOptions(p));
        }

        internal static void Dpi(ConversionParameters p, int dpi)
        {
            if (dpi <= 0)
            {
                throw new LabelZoomValidationException(nameof(dpi), "DPI must be greater than zero.");
            }

            p.Set("dpi", dpi);
        }

        internal static void Rotation(ConversionParameters p, int rotation)
        {
            // Rejected locally: the server would 400, and this is unambiguously a caller bug.
            if (rotation % 90 != 0)
            {
                throw new LabelZoomValidationException(
                    nameof(rotation), $"Rotation must be a multiple of 90 degrees, but was {rotation}.");
            }

            p.Set("rotation", rotation);
        }

        internal static void Scaling(ConversionParameters p, float percent)
        {
            if (percent <= 0)
            {
                throw new LabelZoomValidationException(nameof(percent), "Scaling must be greater than zero.");
            }

            p.Set("scaling", percent);
        }

        internal static void Darkness(ConversionParameters p, int darkness)
        {
            if (darkness < 0 || darkness > 100)
            {
                throw new LabelZoomValidationException(
                    nameof(darkness), $"Darkness must be between 0 and 100, but was {darkness}.");
            }

            p.Set("darkness", darkness);
        }

        internal static void Position(ConversionParameters p, int x, int y)
        {
            p.SetNested("position", "x", x);
            p.SetNested("position", "y", y);
        }

        internal static void Dialect(ConversionParameters p, string dialect)
        {
            if (string.IsNullOrWhiteSpace(dialect))
            {
                throw new LabelZoomValidationException(nameof(dialect), "Dialect cannot be null or empty.");
            }

            p.Set("dialect", dialect);
        }

        internal static void ZplImageCompression(ConversionParameters p, ZplImageCompression compression) =>
            p.SetNested("zpl", "imageCompression", compression.ToWireToken());

        internal static void ZplCollapseCopies(ConversionParameters p, bool collapse) =>
            p.SetNested("zpl", "collapseCopies", collapse);

        internal static void Layout(ConversionParameters p, Action<LayoutOptions> configure)
        {
            if (configure is null)
            {
                throw new LabelZoomValidationException(nameof(configure), "Layout configuration cannot be null.");
            }

            configure(new LayoutOptions(p));
        }

        internal static void Parameter(ConversionParameters p, string key, object? value)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new LabelZoomValidationException(nameof(key), "Parameter key cannot be null or empty.");
            }

            p.Set(key, value);
        }

        internal static void RawQueryParameter(ConversionParameters p, string key, string value)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new LabelZoomValidationException(nameof(key), "Query parameter key cannot be null or empty.");
            }

            p.SetRawQueryParameter(key, value ?? string.Empty);
        }
    }
}
