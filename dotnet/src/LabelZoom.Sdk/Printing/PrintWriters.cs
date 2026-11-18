using System;

namespace LabelZoom.Sdk.Printing
{
    /// <summary>
    /// The print options' validation and wire spelling, written once.
    /// </summary>
    /// <remarks>
    /// The printer target stages call into these the way the convert stages call into
    /// <c>ParameterWriters</c>, so a document print and a template print can't validate
    /// <c>copies</c> differently. Source options go through <c>ParameterWriters</c> itself.
    /// </remarks>
    internal static class PrintWriters
    {
        internal const string JsonMediaType = "application/json";

        internal const int MinCopies = 1;

        internal const int MaxCopies = 9999;

        /// <summary>
        /// Every source prints except a URL and base64 text (contract P2). Print doesn't fetch, and it
        /// reads the body as the format's own bytes.
        /// </summary>
        internal static Conversion.ConversionDraft RequirePrintable(Conversion.ConversionDraft draft)
        {
            if (draft.Source == SourceFormat.Url)
            {
                throw new LabelZoomValidationException(
                    "source", "A URL can't be printed: print doesn't fetch. Fetch the document and print its bytes.");
            }

            if (draft.IsBase64Text)
            {
                throw new LabelZoomValidationException(
                    "source", "Base64 text can't be printed: print reads the document's own bytes. Decode it first.");
            }

            return draft;
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
