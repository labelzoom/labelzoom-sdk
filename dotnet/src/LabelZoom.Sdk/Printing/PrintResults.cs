using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace LabelZoom.Sdk.Printing
{
    /// <summary>A print job the server accepted.</summary>
    public sealed class PrintJob
    {
        internal PrintJob(string jobId, string status, int? copies, bool idempotent)
        {
            JobId = jobId;
            Status = status;
            Copies = copies;
            Idempotent = idempotent;
        }

        /// <summary>The job's id. Pass it to <see cref="PrintJobsClient.GetAsync"/> to follow the job.</summary>
        public string JobId { get; }

        /// <summary>
        /// <c>dispatched</c> when an online agent took the job, or <c>queued</c> when none is online
        /// and it prints when one reconnects. Kept as the server's string, since statuses can be added.
        /// </summary>
        public string Status { get; }

        /// <summary>The copies requested, or <c>null</c> when the request didn't set any.</summary>
        public int? Copies { get; }

        /// <summary>
        /// True when the request reused an Idempotency-Key: this is the original job, and nothing
        /// printed again.
        /// </summary>
        public bool Idempotent { get; }

        internal static PrintJob FromJson(JsonElement json) => new PrintJob(
            PrintResponses.String(json, "jobId") ?? string.Empty,
            PrintResponses.String(json, "status") ?? string.Empty,
            PrintResponses.Int(json, "copies"),
            json.TryGetProperty("idempotent", out var idempotent) && idempotent.ValueKind == JsonValueKind.True);
    }

    /// <summary>The jobs a template print created: one per record.</summary>
    public sealed class PrintBatch
    {
        internal PrintBatch(IReadOnlyList<PrintJob> jobs, int remaining, string? message)
        {
            Jobs = jobs;
            Remaining = remaining;
            Message = message;
        }

        /// <summary>The jobs submitted, in record order.</summary>
        public IReadOnlyList<PrintJob> Jobs { get; }

        /// <summary>
        /// How many records at the end of the batch weren't submitted because the request ran out of
        /// time. Send the same request with the same Idempotency-Key to submit them: the records
        /// already submitted replay without printing again.
        /// </summary>
        public int Remaining { get; }

        /// <summary>Whether every record was submitted.</summary>
        public bool IsComplete => Remaining == 0;

        /// <summary>The server's explanation when <see cref="Remaining"/> isn't 0; otherwise <c>null</c>.</summary>
        public string? Message { get; }

        internal static PrintBatch FromJson(JsonElement json)
        {
            // One record comes back as a bare job, several as {"jobs": [...]}. Callers get one shape.
            if (!json.TryGetProperty("jobs", out var jobs) || jobs.ValueKind != JsonValueKind.Array)
            {
                return new PrintBatch(new[] { PrintJob.FromJson(json) }, 0, null);
            }

            var list = new List<PrintJob>();
            foreach (var job in jobs.EnumerateArray())
            {
                list.Add(PrintJob.FromJson(job));
            }

            return new PrintBatch(
                list, PrintResponses.Int(json, "remaining") ?? 0, PrintResponses.String(json, "message"));
        }
    }

    /// <summary>A print job's current state, from <see cref="PrintJobsClient.GetAsync"/>.</summary>
    public sealed class PrintJobDetails
    {
        private PrintJobDetails(JsonElement json)
        {
            Id = PrintResponses.String(json, "id") ?? string.Empty;
            PrinterId = PrintResponses.String(json, "printer_id");
            AgentId = PrintResponses.String(json, "agent_id");
            SourceFormat = PrintResponses.String(json, "source_format");
            Copies = PrintResponses.Int(json, "copies");
            Status = PrintResponses.String(json, "status") ?? string.Empty;
            Error = PrintResponses.String(json, "error");
            RetryCount = PrintResponses.Int(json, "retry_count") ?? 0;
            CreatedDate = PrintResponses.Instant(json, "created_date");
            DispatchedDate = PrintResponses.Instant(json, "dispatched_date");
            CompletedDate = PrintResponses.Instant(json, "completed_date");
            RetriedDate = PrintResponses.Instant(json, "retried_date");
        }

        /// <summary>The job's id.</summary>
        public string Id { get; }

        /// <summary>The printer the job is for.</summary>
        public string? PrinterId { get; }

        /// <summary>The agent that took the job, or <c>null</c> while it is queued.</summary>
        public string? AgentId { get; }

        /// <summary>The format the job was stored in: the printer's language after any conversion.</summary>
        public string? SourceFormat { get; }

        /// <summary>The copies requested, or <c>null</c> when the request didn't set any.</summary>
        public int? Copies { get; }

        /// <summary><c>queued</c>, <c>dispatched</c>, <c>printing</c>, <c>completed</c> or <c>failed</c>.</summary>
        public string Status { get; }

        /// <summary>Why the job failed, or <c>null</c>.</summary>
        public string? Error { get; }

        /// <summary>How many times the job was retried from the dashboard.</summary>
        public int RetryCount { get; }

        /// <summary>When the job was created.</summary>
        public DateTimeOffset? CreatedDate { get; }

        /// <summary>When an agent took the job, or <c>null</c>.</summary>
        public DateTimeOffset? DispatchedDate { get; }

        /// <summary>When the job completed or failed, or <c>null</c>.</summary>
        public DateTimeOffset? CompletedDate { get; }

        /// <summary>When the job was last retried, or <c>null</c>.</summary>
        public DateTimeOffset? RetriedDate { get; }

        internal static PrintJobDetails FromJson(JsonElement json) => new PrintJobDetails(json);
    }

    /// <summary>The account's Cloud Print limits and usage, from <see cref="LabelZoomClient.GetPrintQuotaAsync"/>.</summary>
    public sealed class PrintQuota
    {
        private PrintQuota(JsonElement json)
        {
            Plan = PrintResponses.String(json, "plan") ?? string.Empty;
            Agents = PrintQuotaCounter.FromJson(json, "agents");
            Printers = PrintQuotaCounter.FromJson(json, "printers");
            JobsToday = PrintQuotaCounter.FromJson(json, "jobsToday");
            Limits = new PrintQuotaLimits(json.TryGetProperty("limits", out var limits) ? limits : default);
        }

        /// <summary>The account's print plan.</summary>
        public string Plan { get; }

        /// <summary>Enrolled agents. Unused enrollment codes count in <see cref="PrintQuotaCounter.Pending"/>.</summary>
        public PrintQuotaCounter Agents { get; }

        /// <summary>Printers added.</summary>
        public PrintQuotaCounter Printers { get; }

        /// <summary>Jobs created today. The count resets at <see cref="PrintQuotaCounter.ResetAt"/>, 00:00 UTC.</summary>
        public PrintQuotaCounter JobsToday { get; }

        /// <summary>The size limits for a print request.</summary>
        public PrintQuotaLimits Limits { get; }

        internal static PrintQuota FromJson(JsonElement json) => new PrintQuota(json);
    }

    /// <summary>One counted quota: how much is used, the limit, and what remains.</summary>
    public sealed class PrintQuotaCounter
    {
        private PrintQuotaCounter(JsonElement json)
        {
            Used = PrintResponses.Int(json, "used") ?? 0;
            Limit = PrintResponses.Int(json, "limit") ?? 0;
            Remaining = PrintResponses.Int(json, "remaining") ?? 0;
            Pending = PrintResponses.Int(json, "pending");
            ResetAt = PrintResponses.Instant(json, "resetAt");
        }

        /// <summary>How many are used.</summary>
        public int Used { get; }

        /// <summary>The plan's limit.</summary>
        public int Limit { get; }

        /// <summary>How many remain.</summary>
        public int Remaining { get; }

        /// <summary>For agents only: unused enrollment codes, which hold a slot until they expire.</summary>
        public int? Pending { get; }

        /// <summary>For jobs today only: when the count resets.</summary>
        public DateTimeOffset? ResetAt { get; }

        internal static PrintQuotaCounter FromJson(JsonElement parent, string name) =>
            new PrintQuotaCounter(parent.TryGetProperty(name, out var json) ? json : default);
    }

    /// <summary>The size limits for a print request.</summary>
    public sealed class PrintQuotaLimits
    {
        internal PrintQuotaLimits(JsonElement json)
        {
            MaxPayloadBytes = PrintResponses.Long(json, "maxPayloadBytes") ?? 0;
            MaxStoredPayloadBytes = PrintResponses.Long(json, "maxStoredPayloadBytes") ?? 0;
            MaxBatchSize = PrintResponses.Int(json, "maxBatchSize") ?? 0;
        }

        /// <summary>The largest document a print request can send.</summary>
        public long MaxPayloadBytes { get; }

        /// <summary>The largest document after conversion to the printer's language.</summary>
        public long MaxStoredPayloadBytes { get; }

        /// <summary>The most records one template print can send.</summary>
        public int MaxBatchSize { get; }
    }

    /// <summary>Reads print responses. Unknown members are ignored, so the server can add them.</summary>
    internal static class PrintResponses
    {
        internal static async Task<T> ReadAsync<T>(HttpResponseMessage response, Func<JsonElement, T> read)
        {
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var document = JsonDocument.Parse(body);
            return read(document.RootElement);
        }

        internal static string? String(JsonElement json, string name) =>
            json.ValueKind == JsonValueKind.Object &&
            json.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        internal static int? Int(JsonElement json, string name) =>
            json.ValueKind == JsonValueKind.Object &&
            json.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt32(out var number)
                ? number
                : (int?)null;

        internal static long? Long(JsonElement json, string name) =>
            json.ValueKind == JsonValueKind.Object &&
            json.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt64(out var number)
                ? number
                : (long?)null;

        internal static DateTimeOffset? Instant(JsonElement json, string name)
        {
            var text = String(json, name);
            return text is not null &&
                   DateTimeOffset.TryParse(
                       text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var instant)
                ? instant
                : (DateTimeOffset?)null;
        }
    }
}
