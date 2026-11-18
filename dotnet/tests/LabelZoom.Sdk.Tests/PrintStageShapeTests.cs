using System.Net;
using System.Reflection;
using LabelZoom.Sdk.Conversion;
using LabelZoom.Sdk.Printing;

namespace LabelZoom.Sdk.Tests;

/// <summary>
/// Pins the printer target (contract section 1 and P2): a printer is reached from every source stage
/// through <c>ToPrinter</c>, and its stage offers only the options that apply to a printer.
/// </summary>
public class PrintStageShapeTests
{
    public static TheoryData<Type> SourceStages => new()
    {
        typeof(SourceBuilder), typeof(PrinterLanguageSourceBuilder), typeof(ZplSourceBuilder),
        typeof(ImageSourceBuilder), typeof(PdfSourceBuilder), typeof(DynamicSourceBuilder),
    };

    [Theory]
    [MemberData(nameof(SourceStages))]
    public void EverySourceStageCanTargetAPrinter(Type stage)
    {
        var toPrinter = stage.GetMethod("ToPrinter", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(toPrinter);
        Assert.Equal(typeof(PrinterTargetBuilder), toPrinter!.ReturnType);
    }

    [Fact]
    public void ThePrinterTargetOffersOnlyPrinterOptions()
    {
        Assert.Equal(
            new[] { "ExecuteAsync", "WithCopies", "WithIdempotencyKey", "WithRotation" },
            PublicMethods(typeof(PrinterTargetBuilder)));
        Assert.Equal(typeof(Task<PrintJob>), typeof(PrinterTargetBuilder).GetMethod("ExecuteAsync")!.ReturnType);
    }

    [Fact]
    public void TheTemplateSourceTakesDataAndTargetsOnlyAPrinter()
    {
        Assert.Equal(new[] { "ToPrinter", "WithData", "WithValidate" }, PublicMethods(typeof(TemplateSourceBuilder)));
        Assert.Equal(
            new[] { "ExecuteAsync", "WithCopies", "WithIdempotencyKey" },
            PublicMethods(typeof(TemplatePrinterTargetBuilder)));
    }

    [Fact]
    public void ANonFetchingPrintRefusesAUrlAndBase64Text()
    {
        using var client = new LabelZoomClient(new LabelZoomClientOptions { ApiKey = "lz_test" });

        var url = Assert.Throws<LabelZoomValidationException>(
            () => client.Convert().FromUrl("https://example.com/label.zpl").ToPrinter("p1"));
        Assert.Equal("source", url.ParamName);

        var base64 = Assert.Throws<LabelZoomValidationException>(
            () => client.Convert().FromBase64Text(SourceFormat.Pdf, "JVBERi0=").ToPrinter("p1"));
        Assert.Equal("source", base64.ParamName);
    }

    [Fact]
    public async Task TypedChainSendsSourceOptionsRotationCopiesAndKey()
    {
        var handler = new StubHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, "{\"jobId\":\"j1\",\"status\":\"queued\",\"copies\":2}", "application/json");
        using var client = new LabelZoomClient(new LabelZoomClientOptions
        {
            ApiKey = "lz_test", HttpMessageHandler = handler, MaxRetries = 0,
        });

        var job = await client.Convert()
            .FromPdf(new byte[] { 0x25, 0x50, 0x44, 0x46 })
                .WithPage(1)
                .WithSplit(s => s.Rows(2))
            .ToPrinter("printer-1")
                .WithRotation(90)
                .WithCopies(2)
                .WithIdempotencyKey("order-42")
            .ExecuteAsync();

        var request = handler.LastRequest;
        Assert.Equal("/api/v3/printers/printer-1/print", request.RequestUri.AbsolutePath);
        Assert.Equal("pdf", request.QueryValue("sourceFormat"));
        Assert.Equal("2", request.QueryValue("copies"));
        Assert.Equal(1, request.Params!.Value.GetProperty("pdf").GetProperty("pageNumber").GetInt32());
        Assert.Equal(2, request.Params!.Value.GetProperty("split").GetProperty("rows").GetInt32());
        Assert.Equal(90, request.Params!.Value.GetProperty("rotation").GetInt32());
        Assert.Equal("order-42", request.Header("idempotency-key"));
        Assert.Equal("application/pdf", request.Header("content-type"));
        Assert.Equal("j1", job.JobId);
        Assert.Equal("queued", job.Status);
        Assert.Equal(2, job.Copies);
        Assert.False(job.Idempotent);
    }

    [Fact]
    public async Task ADesignFilePrintsAsItsOwnBytes()
    {
        var handler = new StubHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, "{\"jobId\":\"j1\",\"status\":\"queued\"}", "application/json");
        using var client = new LabelZoomClient(new LabelZoomClientOptions
        {
            ApiKey = "lz_test", HttpMessageHandler = handler, MaxRetries = 0,
        });

        await client.Convert().FromBtw(new byte[] { 0x00, 0xff, 0x10 }).ToPrinter("p1").ExecuteAsync();

        Assert.Equal("btw", handler.LastRequest.QueryValue("sourceFormat"));
        Assert.Equal("application/octet-stream", handler.LastRequest.Header("content-type"));
        Assert.Equal(new byte[] { 0x00, 0xff, 0x10 }, handler.LastRequest.Body);
    }

    [Fact]
    public async Task EachCallGetsItsOwnGeneratedKey()
    {
        var handler = new StubHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, "{\"jobId\":\"j1\",\"status\":\"queued\"}", "application/json");
        handler.Enqueue(HttpStatusCode.OK, "{\"jobId\":\"j2\",\"status\":\"queued\"}", "application/json");
        using var client = new LabelZoomClient(new LabelZoomClientOptions
        {
            ApiKey = "lz_test", HttpMessageHandler = handler, MaxRetries = 0,
        });

        var print = client.Convert().FromZpl("^XA^XZ").ToPrinter("printer-1");
        await print.ExecuteAsync();
        await print.ExecuteAsync();

        // Two calls are two prints. Only a retry within one call may reuse a key.
        Assert.NotEqual(handler.Requests[0].Header("idempotency-key"), handler.Requests[1].Header("idempotency-key"));
    }

    private static string[] PublicMethods(Type type) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            .Distinct()
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();
}
