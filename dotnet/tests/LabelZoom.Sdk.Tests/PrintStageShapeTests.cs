using System.Net;
using System.Reflection;
using LabelZoom.Sdk.Conversion;
using LabelZoom.Sdk.Printing;

namespace LabelZoom.Sdk.Tests;

/// <summary>
/// Pins the print stage against the convert source stage it mirrors (contract section 1.2).
/// </summary>
/// <remarks>
/// The print builders can't reuse the convert source builders, because those carry the <c>To*</c>
/// transitions and print has no target stage. So each format has a print twin. These tests are what
/// stop the twins drifting: a source option added to <see cref="PdfSourceBuilder"/> and not to
/// <see cref="PdfPrintBuilder"/> fails here.
/// </remarks>
public class PrintStageShapeTests
{
    private static readonly HashSet<string> PrintOnly = new() { "WithCopies", "WithIdempotencyKey", "SendAsync" };

    public static TheoryData<Type, Type> Twins => new()
    {
        { typeof(SourceBuilder), typeof(PrintBuilder) },
        { typeof(PrinterLanguageSourceBuilder), typeof(PrinterLanguagePrintBuilder) },
        { typeof(ZplSourceBuilder), typeof(ZplPrintBuilder) },
        { typeof(ImageSourceBuilder), typeof(ImagePrintBuilder) },
        { typeof(PdfSourceBuilder), typeof(PdfPrintBuilder) },
        { typeof(DynamicSourceBuilder), typeof(DynamicPrintBuilder) },
    };

    [Theory]
    [MemberData(nameof(Twins))]
    public void PrintTwinOffersExactlyTheConvertSourceOptions(Type convert, Type print)
    {
        var convertOptions = Options(convert).Where(name => !name.StartsWith("To", StringComparison.Ordinal));
        var printOptions = Options(print).Where(name => !PrintOnly.Contains(name));

        Assert.Equal(convertOptions.OrderBy(n => n), printOptions.OrderBy(n => n));
    }

    [Theory]
    [MemberData(nameof(Twins))]
    public void PrintTwinHasNoTargetStage(Type _, Type print)
    {
        Assert.DoesNotContain(Options(print), name => name.StartsWith("To", StringComparison.Ordinal));
        Assert.Contains("SendAsync", Options(print));
    }

    [Fact]
    public void PrintOffersFromMethodsOnlyForPrintableFormats()
    {
        var methods = typeof(PrintRequestBuilder).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(m => m.Name)
            .ToHashSet();

        foreach (var notPrintable in new[]
                 { "FromUrl", "FromBase64Text", "FromEscLabel", "FromEzpl", "FromTpcl", "FromNlbl", "FromLbl", "FromBtw", "FromDymo" })
        {
            Assert.DoesNotContain(notPrintable, methods);
        }

        foreach (var printable in new[] { "FromZpl", "FromPdf", "FromPng", "FromFingerprint", "FromCpcl", "FromTemplate" })
        {
            Assert.Contains(printable, methods);
        }
    }

    [Fact]
    public async Task TypedPrintChainSendsSourceOptionsCopiesAndKey()
    {
        var handler = new StubHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, "{\"jobId\":\"j1\",\"status\":\"queued\",\"copies\":2}", "application/json");
        using var client = new LabelZoomClient(new LabelZoomClientOptions
        {
            ApiKey = "lz_test", HttpMessageHandler = handler, MaxRetries = 0,
        });

        var job = await client.Print("printer-1")
            .FromPdf(new byte[] { 0x25, 0x50, 0x44, 0x46 })
            .WithPage(1)
            .WithSplit(s => s.Rows(2))
            .WithCopies(2)
            .WithIdempotencyKey("order-42")
            .SendAsync();

        var request = handler.LastRequest;
        Assert.Equal("/api/v3/printers/printer-1/print", request.RequestUri.AbsolutePath);
        Assert.Equal("pdf", request.QueryValue("sourceFormat"));
        Assert.Equal("2", request.QueryValue("copies"));
        Assert.Equal(1, request.Params!.Value.GetProperty("pdf").GetProperty("pageNumber").GetInt32());
        Assert.Equal(2, request.Params!.Value.GetProperty("split").GetProperty("rows").GetInt32());
        Assert.Equal("order-42", request.Header("idempotency-key"));
        Assert.Equal("application/pdf", request.Header("content-type"));
        Assert.Equal("j1", job.JobId);
        Assert.Equal("queued", job.Status);
        Assert.Equal(2, job.Copies);
        Assert.False(job.Idempotent);
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

        var print = client.Print("printer-1").FromZpl("^XA^XZ");
        await print.SendAsync();
        await print.SendAsync();

        // Two calls are two prints. Only a retry within one call may reuse a key.
        Assert.NotEqual(handler.Requests[0].Header("idempotency-key"), handler.Requests[1].Header("idempotency-key"));
    }

    private static IEnumerable<string> Options(Type type) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.DeclaringType != typeof(object))
            .Select(m => m.Name)
            .Distinct();
}
