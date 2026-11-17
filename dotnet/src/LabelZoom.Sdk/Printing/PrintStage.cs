using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using LabelZoom.Sdk.Conversion;

namespace LabelZoom.Sdk.Printing
{
    /// <summary>A document print under construction.</summary>
    internal sealed class PrintDraft
    {
        internal PrintDraft(LabelZoomClient client, string printerId, SourceFormat source, byte[] body)
        {
            Client = client;
            PrinterId = printerId;
            Source = source;
            Body = body;
        }

        internal LabelZoomClient Client { get; }

        internal string PrinterId { get; }

        internal SourceFormat Source { get; }

        internal byte[] Body { get; }

        /// <summary>The source options, serialized as the convert path serializes them.</summary>
        internal ConversionParameters Parameters { get; } = new ConversionParameters();

        internal int? Copies { get; set; }

        internal string? IdempotencyKey { get; set; }
    }

    /// <summary>
    /// The print source stage: options that change how the document is read, then the print options,
    /// then <see cref="SendAsync"/>.
    /// </summary>
    /// <remarks>
    /// It mirrors <see cref="SourceStage{TSelf}"/> without the <c>To*</c> transitions, because the
    /// printer picks the output language. Each format's print builder offers exactly the source options
    /// its convert builder does, through the same shared writers; a stage-shape test holds the two
    /// sets equal. Options the server doesn't apply to the format are ignored rather than rejected.
    /// </remarks>
    /// <typeparam name="TSelf">The concrete builder, so each method returns it unchanged.</typeparam>
    public abstract class PrintStage<TSelf>
        where TSelf : PrintStage<TSelf>
    {
        private protected PrintStage(PrintDraft draft) => Draft = draft;

        private protected PrintDraft Draft { get; }

        private protected ConversionParameters Parameters => Draft.Parameters;

        private protected TSelf Self => (TSelf)this;

        /// <summary>
        /// Replaces the label size the document declares, in <b>inches</b>. Content past it is clipped,
        /// as it is on the printer. Omit it to use the declared size, or else the content's extent.
        /// </summary>
        public TSelf WithLabelSize(float widthInches, float heightInches)
        {
            ParameterWriters.LabelSize(Parameters, widthInches, heightInches);
            return Self;
        }

        /// <summary>Prints only the label at this <b>0-based</b> index.</summary>
        public TSelf WithLabelIndex(int zeroBasedIndex)
        {
            ParameterWriters.LabelIndex(Parameters, zeroBasedIndex);
            return Self;
        }

        /// <summary>Fills the document's variable fields. One record prints one label.</summary>
        public TSelf WithData(params object?[] records)
        {
            ParameterWriters.Data(Parameters, records);
            return Self;
        }

        /// <summary>
        /// Sends the whole job to the printer this many times, from 1 to 9999. It multiplies a quantity
        /// in the document: <c>^PQ3</c> with 5 copies prints 15 labels.
        /// </summary>
        public TSelf WithCopies(int copies)
        {
            Draft.Copies = PrintWriters.Copies(copies);
            return Self;
        }

        /// <summary>
        /// Makes the print safe to repeat: a second request with the same key returns the original job
        /// instead of printing again. Use something unique to the label, such as an order number.
        /// </summary>
        /// <remarks>
        /// Without it the SDK generates a key for each call, which makes its own retries safe but not a
        /// repeat of the whole call. Keys are scoped to the account; a deliberate reprint needs a new one.
        /// </remarks>
        public TSelf WithIdempotencyKey(string key)
        {
            Draft.IdempotencyKey = PrintWriters.IdempotencyKey(key);
            return Self;
        }

        /// <summary>Sends the document to the printer.</summary>
        public Task<PrintJob> SendAsync(CancellationToken cancellationToken = default)
        {
            var draft = Draft;
            var query = new List<string> { "sourceFormat=" + draft.Source.ToWireToken() };
            if (draft.Copies.HasValue)
            {
                query.Add("copies=" + draft.Copies.Value);
            }

            // An empty params would make the server convert a document already in the printer's
            // language, so it is sent only when a source option was set (contract P2).
            var json = draft.Parameters.ToJson();
            if (json is not null)
            {
                query.Add("params=" + System.Uri.EscapeDataString(json));
            }

            var client = draft.Client;
            var uri = client.ApiUri(
                $"/api/v3/printers/{PrintWriters.Segment(draft.PrinterId)}/print?{string.Join("&", query)}");
            var key = PrintWriters.KeyForCall(draft.IdempotencyKey);
            var contentType = draft.Source.ToMediaType();

            return client.SendAsync(
                () =>
                {
                    var request = client.CreateRequest(
                        HttpMethod.Post, uri, PrintWriters.JsonMediaType, draft.Body, contentType);
                    request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
                    return request;
                },
                response => PrintResponses.ReadAsync(response, PrintJob.FromJson),
                cancellationToken);
        }
    }
}
