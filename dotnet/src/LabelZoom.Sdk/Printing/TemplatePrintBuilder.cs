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
    /// <summary>Prints a published template, one label per record.</summary>
    /// <remarks>
    /// A template is already a label, so there are no source options. Each label is converted to the
    /// printer's language.
    /// </remarks>
    public sealed class TemplatePrintBuilder
    {
        private readonly LabelZoomClient _client;
        private readonly string _printerId;
        private readonly string _templateId;
        private List<JsonElement> _records = new List<JsonElement>();
        private bool _validate;
        private int? _copies;
        private string? _idempotencyKey;

        internal TemplatePrintBuilder(LabelZoomClient client, string printerId, string templateId)
        {
            _client = client;
            _printerId = printerId;
            _templateId = templateId;
        }

        /// <summary>
        /// The merge-field values: one object per label, whose keys are the template's merge fields. A
        /// template with no merge fields needs none. A batch is limited by the plan's
        /// <see cref="PrintQuotaLimits.MaxBatchSize"/>.
        /// </summary>
        public TemplatePrintBuilder WithData(params object?[] records)
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
        public TemplatePrintBuilder WithValidate(bool validate = true)
        {
            _validate = validate;
            return this;
        }

        /// <summary>Sends each label to the printer this many times, from 1 to 9999.</summary>
        public TemplatePrintBuilder WithCopies(int copies)
        {
            _copies = PrintWriters.Copies(copies);
            return this;
        }

        /// <summary>
        /// Makes the print safe to repeat. In a batch the server gives each record its own key, derived
        /// from this one, so a repeated batch prints only the records that didn't print before.
        /// </summary>
        public TemplatePrintBuilder WithIdempotencyKey(string key)
        {
            _idempotencyKey = PrintWriters.IdempotencyKey(key);
            return this;
        }

        /// <summary>Sends the labels to the printer.</summary>
        /// <returns>
        /// The jobs created. If <see cref="PrintBatch.Remaining"/> isn't 0, send the same request again
        /// with the same Idempotency-Key to submit the rest.
        /// </returns>
        public Task<PrintBatch> SendAsync(CancellationToken cancellationToken = default)
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
