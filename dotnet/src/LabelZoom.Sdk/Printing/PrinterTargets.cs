using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LabelZoom.Sdk.Conversion;

namespace LabelZoom.Sdk.Printing
{
    /// <summary>
    /// The printer target stage, after <see cref="SourceStage{TSelf}.ToPrinter"/>: where the document
    /// goes, how many times, and the key that makes the print safe to repeat.
    /// </summary>
    /// <remarks>
    /// The printer's configuration sets the output language, so the only target option is
    /// <see cref="WithRotation"/>, which turns the finished label whatever the language.
    /// </remarks>
    public sealed class PrinterTargetBuilder
    {
        private readonly ConversionDraft _draft;
        private readonly string _printerId;
        private int? _copies;
        private string? _idempotencyKey;

        internal PrinterTargetBuilder(ConversionDraft draft, string printerId)
        {
            _draft = draft;
            _printerId = printerId;
        }

        /// <summary>Rotation in degrees. Must be a multiple of 90. The server default is 0.</summary>
        public PrinterTargetBuilder WithRotation(int rotation)
        {
            ParameterWriters.Rotation(_draft.Parameters, rotation);
            return this;
        }

        /// <summary>
        /// Sends the whole job to the printer this many times, from 1 to 9999. It multiplies a quantity
        /// in the document: <c>^PQ3</c> with 5 copies prints 15 labels.
        /// </summary>
        public PrinterTargetBuilder WithCopies(int copies)
        {
            _copies = PrintWriters.Copies(copies);
            return this;
        }

        /// <summary>
        /// Makes the print safe to repeat: a second request with the same key returns the original job
        /// instead of printing again. Use something unique to the label, such as an order number.
        /// </summary>
        /// <remarks>
        /// Without it the SDK generates a key for each call, which makes its own retries safe but not a
        /// repeat of the whole call. Keys are scoped to the account; a deliberate reprint needs a new one.
        /// </remarks>
        public PrinterTargetBuilder WithIdempotencyKey(string key)
        {
            _idempotencyKey = PrintWriters.IdempotencyKey(key);
            return this;
        }

        /// <summary>Sends the document to the printer.</summary>
        public Task<PrintJob> ExecuteAsync(CancellationToken cancellationToken = default)
        {
            var query = new List<string> { "sourceFormat=" + _draft.Source.ToWireToken() };
            if (_copies.HasValue)
            {
                query.Add("copies=" + _copies.Value);
            }

            // An empty params would make the server convert a document already in the printer's
            // language, so it is sent only when an option was set (contract P2).
            var json = _draft.Parameters.ToJson();
            if (json is not null)
            {
                query.Add("params=" + System.Uri.EscapeDataString(json));
            }

            var client = _draft.Client;
            var uri = client.ApiUri(
                $"/api/v3/printers/{PrintWriters.Segment(_printerId)}/print?{string.Join("&", query)}");
            var key = PrintWriters.KeyForCall(_idempotencyKey);
            var body = _draft.Body;
            var contentType = _draft.ContentType;

            return client.SendAsync(
                () =>
                {
                    var request = client.CreateRequest(HttpMethod.Post, uri, PrintWriters.JsonMediaType, body, contentType);
                    request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
                    return request;
                },
                response => PrintResponses.ReadAsync(response, PrintJob.FromJson),
                cancellationToken);
        }
    }

    /// <summary>
    /// The source stage for a published template, after <see cref="ConversionRequestBuilder.FromTemplate"/>.
    /// </summary>
    /// <remarks>
    /// A template is already a label, so it takes merge data instead of source options. Its only target
    /// today is a printer.
    /// </remarks>
    public sealed class TemplateSourceBuilder
    {
        private readonly LabelZoomClient _client;
        private readonly string _templateId;
        private List<JsonElement> _records = new List<JsonElement>();
        private bool _validate;

        internal TemplateSourceBuilder(LabelZoomClient client, string templateId)
        {
            _client = client;
            _templateId = templateId;
        }

        /// <summary>
        /// The merge-field values: one object per label, whose keys are the template's merge fields. A
        /// template with no merge fields needs none. A batch is limited by the plan's
        /// <see cref="PrintQuotaLimits.MaxBatchSize"/>.
        /// </summary>
        public TemplateSourceBuilder WithData(params object?[] records)
        {
            if (records is null || records.Length == 0)
            {
                throw new LabelZoomValidationException(
                    "data", "Provide at least one record, or omit WithData for a template with no merge fields.");
            }

            _records = ConversionParameters.NormalizeData(records);
            return this;
        }

        /// <summary>
        /// Checks every record for missing merge fields before anything prints. A missing field fails the
        /// whole request with a <see cref="LabelZoomUnprocessableException"/>.
        /// </summary>
        public TemplateSourceBuilder WithValidate(bool validate = true)
        {
            _validate = validate;
            return this;
        }

        /// <summary>Prints one label per record on a Cloud Print printer, converted to its language.</summary>
        /// <param name="printerId">The printer's id, from the Printers page.</param>
        public TemplatePrinterTargetBuilder ToPrinter(string printerId) => new TemplatePrinterTargetBuilder(
            _client, _templateId, PrintWriters.RequireId(printerId, nameof(printerId)), _records, _validate);
    }

    /// <summary>The printer target stage for a template, after <see cref="TemplateSourceBuilder.ToPrinter"/>.</summary>
    public sealed class TemplatePrinterTargetBuilder
    {
        private readonly LabelZoomClient _client;
        private readonly string _templateId;
        private readonly string _printerId;
        private readonly IReadOnlyList<JsonElement> _records;
        private readonly bool _validate;
        private int? _copies;
        private string? _idempotencyKey;

        internal TemplatePrinterTargetBuilder(
            LabelZoomClient client, string templateId, string printerId, IReadOnlyList<JsonElement> records, bool validate)
        {
            _client = client;
            _templateId = templateId;
            _printerId = printerId;
            _records = records;
            _validate = validate;
        }

        /// <summary>Sends each label to the printer this many times, from 1 to 9999.</summary>
        public TemplatePrinterTargetBuilder WithCopies(int copies)
        {
            _copies = PrintWriters.Copies(copies);
            return this;
        }

        /// <summary>
        /// Makes the print safe to repeat. In a batch the server gives each record its own key, derived
        /// from this one, so a repeated batch prints only the records that didn't print before.
        /// </summary>
        public TemplatePrinterTargetBuilder WithIdempotencyKey(string key)
        {
            _idempotencyKey = PrintWriters.IdempotencyKey(key);
            return this;
        }

        /// <summary>Sends the labels to the printer.</summary>
        /// <returns>
        /// The jobs created. If <see cref="PrintBatch.Remaining"/> isn't 0, send the same request again
        /// with the same Idempotency-Key to submit the rest.
        /// </returns>
        public Task<PrintBatch> ExecuteAsync(CancellationToken cancellationToken = default)
        {
            var query = new List<string>();
            if (_copies.HasValue)
            {
                query.Add("copies=" + _copies.Value);
            }

            if (_validate)
            {
                query.Add("validate=true");
            }

            var path = $"/api/v3/printers/{PrintWriters.Segment(_printerId)}" +
                       $"/templates/{PrintWriters.Segment(_templateId)}/print";
            var uri = _client.ApiUri(query.Count == 0 ? path : path + "?" + string.Join("&", query));
            var body = Encoding.UTF8.GetBytes(Body(_records));
            var key = PrintWriters.KeyForCall(_idempotencyKey);
            var client = _client;

            return client.SendAsync(
                () =>
                {
                    var request = client.CreateRequest(
                        HttpMethod.Post, uri, PrintWriters.JsonMediaType, body, PrintWriters.JsonMediaType);
                    request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
                    return request;
                },
                response => PrintResponses.ReadAsync(response, PrintBatch.FromJson),
                cancellationToken);
        }

        /// <summary>One record is sent as an object and several as an array (contract P3).</summary>
        private static string Body(IReadOnlyList<JsonElement> records) => records.Count switch
        {
            0 => "{}",
            1 => records[0].GetRawText(),
            _ => "[" + string.Join(",", records.Select(r => r.GetRawText())) + "]",
        };
    }
}
