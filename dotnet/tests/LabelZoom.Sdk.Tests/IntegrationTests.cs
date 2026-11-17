using System.Text;

namespace LabelZoom.Sdk.Tests;

/// <summary>
/// Tests that call the real API.
/// </summary>
/// <remarks>
/// <para>
/// Excluded from the pull-request suite via <c>--filter "Category!=Integration"</c>, and run
/// separately by the <c>Live API Tests</c> workflow. They must never run on <c>pull_request</c>:
/// a fork PR has no secrets, and the previous suite's habit of <em>throwing</em> when
/// <c>LABELZOOM_API_TOKEN</c> was unset meant fork contributions could never go green.
/// </para>
/// <para>
/// Their job is to catch server contract drift, not to re-test the SDK — everything else is
/// covered offline by the conformance suite.
/// </para>
/// <para>
/// Every test but one authenticates with <c>LABELZOOM_API_KEY</c>, the way a paying caller
/// does, and the workflow fails before running them if that secret is missing. One test,
/// <see cref="Anonymous_ZplToPng_Succeeds"/>, stays anonymous on purpose: the free tier is a
/// supported configuration and the first thing a new user hits, so it keeps its own watch.
/// Run locally without the variable set and the keyed tests fall back to anonymous, so they
/// still pass.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class IntegrationTests
{
    private const string SampleZpl = "^XA^FO20,20^A0N,28^FDLabelZoom SDK^FS^XZ";

    // Leaves ApiKey null, so the SDK reads LABELZOOM_API_KEY itself: the same path a caller's
    // own configuration takes. Unset, it is anonymous.
    private static LabelZoomClient NewClient() => new(new LabelZoomClientOptions());

    // Deliberately anonymous, whatever the environment holds. Only the free-tier canary uses it.
    private static LabelZoomClient NewAnonymousClient() =>
        new(new LabelZoomClientOptions { ApiKey = string.Empty });

    /// <summary>The free-tier canary: the one test that always runs without a key.</summary>
    [Fact]
    public async Task Anonymous_ZplToPng_Succeeds()
    {
        using var client = NewAnonymousClient();

        var result = await client.Convert().FromZpl(SampleZpl).ToPng().ExecuteAsync();

        Assert.Equal("image/png", result.ContentType);
        // PNG magic number: the response really is an image, not an error page with a 200.
        Assert.True(result.Bytes.Length > 8);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, result.Bytes.Take(4).ToArray());
    }

    [Fact]
    public async Task ZplToPdf_Succeeds()
    {
        using var client = NewClient();

        var result = await client.Convert().FromZpl(SampleZpl).ToPdf().ExecuteAsync();

        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(result.Bytes, 0, 4));
    }

    [Fact]
    public async Task PdfToZpl_Succeeds()
    {
        using var client = NewClient();

        var result = await client.Convert()
            .FromPdf(File.ReadAllBytes(Path.Combine("TestData", "4x6_document.pdf")))
                .WithPage(0)
            .ToZpl()
            .ExecuteAsync();

        Assert.Contains("^XA", result.Text);
        Assert.Contains("^XZ", result.Text);
    }

    /// <summary>
    /// The regression guard for the rule that costs the most to get wrong.
    /// </summary>
    /// <remarks>
    /// GIF, BMP and JPEG are missing from the server's <c>produces</c> list, so naming the exact
    /// target media type in <c>Accept</c> returns 406. The SDK always sends <c>*/*</c>. If someone
    /// ever "tidies" that into an exact media type, these three fail immediately.
    /// </remarks>
    [Theory]
    [InlineData(TargetFormat.Gif, "image/gif")]
    [InlineData(TargetFormat.Bmp, "image/bmp")]
    [InlineData(TargetFormat.Jpeg, "image/jpeg")]
    public async Task ImageTargets_AreNotRejectedByContentNegotiation(
        TargetFormat target, string expectedContentType)
    {
        using var client = NewClient();

        var result = await client.Convert().FromZpl(SampleZpl).To(target).ExecuteAsync();

        Assert.Equal(expectedContentType, result.ContentType);
        Assert.NotEmpty(result.Bytes);
    }

    [Fact]
    public async Task RequestId_IsSurfacedOnSuccess()
    {
        using var client = NewClient();

        var result = await client.Convert().FromZpl(SampleZpl).ToPng().ExecuteAsync();

        Assert.False(
            string.IsNullOrWhiteSpace(result.RequestId),
            "X-LZ-Request-Id should be present on every conversion response; it is the support handle.");
    }

    [Fact]
    public async Task Dpi_ChangesTheRenderedSize()
    {
        using var client = NewClient();

        var baseline = await client.Convert().FromZpl(SampleZpl).ToPng().ExecuteAsync();
        var higher = await client.Convert().FromZpl(SampleZpl).ToPng().WithDpi(300).ExecuteAsync();

        Assert.True(
            higher.Bytes.Length > baseline.Bytes.Length,
            "A 300 dpi render should be larger than the 203 dpi default; if it is not, the " +
            "params query parameter is probably not reaching the server.");
    }
}
