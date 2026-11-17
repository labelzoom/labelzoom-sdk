using System;
using System.Collections.Generic;

namespace LabelZoom.Sdk.Printing
{
    /// <summary>
    /// The print options' validation and wire spelling, written once.
    /// </summary>
    /// <remarks>
    /// The print stage classes call into these the way the convert stages call into
    /// <c>ParameterWriters</c>, so a document print and a template print can't validate
    /// <c>copies</c> differently. Source options go through <c>ParameterWriters</c> itself.
    /// </remarks>
    internal static class PrintWriters
    {
        internal const string JsonMediaType = "application/json";

        internal const int MinCopies = 1;

        internal const int MaxCopies = 9999;

        /// <summary>
        /// The sources the print endpoint converts to the printer's language (contract P2). The
        /// endpoint sends any other format to the printer as is, so the SDK refuses it.
        /// </summary>
        private static readonly HashSet<SourceFormat> Printable = new HashSet<SourceFormat>
        {
            SourceFormat.Zpl, SourceFormat.Epl, SourceFormat.Ipl, SourceFormat.Tspl,
            SourceFormat.Dpl, SourceFormat.Sbpl, SourceFormat.Fingerprint, SourceFormat.Cpcl,
            SourceFormat.Xml, SourceFormat.Json, SourceFormat.Pdf, SourceFormat.Png,
            SourceFormat.Bmp, SourceFormat.Gif, SourceFormat.Jpeg, SourceFormat.Jpg,
        };

        internal static SourceFormat RequirePrintable(SourceFormat format)
        {
            if (!Printable.Contains(format))
            {
                throw new LabelZoomValidationException(
                    "source",
                    $"{format.ToWireToken()} can't be printed directly: the print endpoint converts only " +
                    "ZPL, EPL, IPL, TSPL, DPL, SBPL, Fingerprint, CPCL, LabelZoom XML and JSON, PDF and " +
                    "images. Convert it to the printer's language with Convert() first, then print the result.");
            }

            return format;
        }

        internal static string RequireId(string? id, string parameter)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new LabelZoomValidationException(parameter, $"{parameter} cannot be null or empty.");
            }

            return id!;
        }

        internal static int Copies(int copies)
        {
            if (copies < MinCopies || copies > MaxCopies)
            {
                throw new LabelZoomValidationException(
                    "copies", $"copies must be from {MinCopies} to {MaxCopies}; got {copies}.");
            }

            return copies;
        }

        internal static string IdempotencyKey(string? key)
        {
            if (key is null || key.Trim().Length == 0)
            {
                throw new LabelZoomValidationException(
                    "idempotencyKey",
                    "An Idempotency-Key cannot be blank. Omit WithIdempotencyKey to have the SDK generate one.");
            }

            return key;
        }

        /// <summary>
        /// The key for one call: the caller's, or a new random UUID. It is fixed before the first
        /// attempt so every retry of the call sends the same one (contract P4).
        /// </summary>
        internal static string KeyForCall(string? callerKey) => callerKey ?? Guid.NewGuid().ToString("D");

        internal static string Segment(string id) => Uri.EscapeDataString(id);
    }
}
