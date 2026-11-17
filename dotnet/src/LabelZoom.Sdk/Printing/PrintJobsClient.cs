using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace LabelZoom.Sdk.Printing
{
    /// <summary>Print jobs, from <see cref="LabelZoomClient.Jobs"/>.</summary>
    public sealed class PrintJobsClient
    {
        private readonly LabelZoomClient _client;

        internal PrintJobsClient(LabelZoomClient client) => _client = client;

        /// <summary>
        /// Gets a job's current state. Poll it to follow a job from <c>queued</c> or <c>dispatched</c>
        /// to <c>completed</c> or <c>failed</c>. Job history is kept for 30 days.
        /// </summary>
        /// <param name="jobId">The <see cref="PrintJob.JobId"/> a print returned.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        public Task<PrintJobDetails> GetAsync(string jobId, CancellationToken cancellationToken = default)
        {
            var uri = _client.ApiUri($"/api/v3/jobs/{PrintWriters.Segment(PrintWriters.RequireId(jobId, nameof(jobId)))}");
            return _client.SendAsync(
                () => _client.CreateRequest(HttpMethod.Get, uri, PrintWriters.JsonMediaType),
                response => PrintResponses.ReadAsync(response, PrintJobDetails.FromJson),
                cancellationToken);
        }
    }
}
