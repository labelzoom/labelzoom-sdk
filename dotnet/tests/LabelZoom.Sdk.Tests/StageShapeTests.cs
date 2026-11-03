using System.Net;
using System.Reflection;
using LabelZoom.Sdk.Conversion;

namespace LabelZoom.Sdk.Tests;

/// <summary>
/// Pins which options each stage of the fluent chain offers.
/// </summary>
/// <remarks>
/// <para>
/// The staged builders exist so that an option can only be set where it acts. <c>FromZpl(...)
/// .WithPage(0)</c> must not compile, and neither may <c>ToZpl().WithPage(0)</c>. A test can't
/// assert a compile error about itself, so these assert what makes the compile error certain: the
/// method isn't a member of that builder.
/// </para>
/// <para>
/// The conformance suite runs through <see cref="DynamicSourceBuilder"/> and
/// <see cref="DynamicTargetBuilder"/>, because its fixtures pick formats at run time. The last test
/// here sends a request through the typed builders, so that path is checked on the wire too.
/// </para>
/// </remarks>
public class StageShapeTests
{
    private static readonly Type[] SourceStages =
    {
        typeof(SourceBuilder), typeof(ZplSourceBuilder), typeof(ImageSourceBuilder),
        typeof(PdfSourceBuilder), typeof(DynamicSourceBuilder),
    };

    private static readonly Type[] TargetStages =
    {
        typeof(TargetBuilder), typeof(ZplTargetBuilder), typeof(DynamicTargetBuilder),
    };

    [Theory]
    [InlineData(typeof(ZplSourceBuilder), "WithPage")]
    [InlineData(typeof(ZplSourceBuilder), "WithSplit")]
    [InlineData(typeof(SourceBuilder), "WithPage")]
    [InlineData(typeof(SourceBuilder), "WithCommandsToIgnore")]
    [InlineData(typeof(SourceBuilder), "WithSplit")]
    [InlineData(typeof(ImageSourceBuilder), "WithPage")]
    [InlineData(typeof(PdfSourceBuilder), "WithCommandsToIgnore")]
    [InlineData(typeof(TargetBuilder), "WithLayout")]
    [InlineData(typeof(TargetBuilder), "WithCollapseCopies")]
    [InlineData(typeof(TargetBuilder), "WithImageCompression")]
    public void FormatSpecificOptionIsAbsentFromOtherFormats(Type builder, string method) =>
        Assert.False(HasMethod(builder, method), $"{builder.Name} must not offer {method}.");

    [Theory]
    [InlineData(typeof(ZplSourceBuilder), "WithCommandsToIgnore")]
    [InlineData(typeof(ImageSourceBuilder), "WithSplit")]
    [InlineData(typeof(PdfSourceBuilder), "WithPage")]
    [InlineData(typeof(PdfSourceBuilder), "WithConversionMode")]
    [InlineData(typeof(PdfSourceBuilder), "WithSplit")]
    [InlineData(typeof(ZplTargetBuilder), "WithLayout")]
    [InlineData(typeof(ZplTargetBuilder), "WithCollapseCopies")]
    [InlineData(typeof(ZplTargetBuilder), "WithImageCompression")]
    public void FormatSpecificOptionIsOnItsFormat(Type builder, string method) =>
        Assert.True(HasMethod(builder, method), $"{builder.Name} must offer {method}.");

    [Theory]
    [InlineData("WithDpi")]
    [InlineData("WithRotation")]
    [InlineData("WithLayout")]
    [InlineData("WithDialect")]
    [InlineData("ExecuteAsync")]
    public void SourceStagesOfferNoTargetOptions(string method)
    {
        foreach (var stage in SourceStages)
        {
            Assert.False(HasMethod(stage, method), $"{stage.Name} must not offer {method}.");
        }
    }

    [Theory]
    [InlineData("WithLabelSize")]
    [InlineData("WithLabelIndex")]
    [InlineData("WithData")]
    [InlineData("WithPage")]
    [InlineData("WithPdfPage")]
    [InlineData("WithSplit")]
    [InlineData("WithZplCommandsToIgnore")]
    public void TargetStagesOfferNoSourceOptions(string method)
    {
        foreach (var stage in TargetStages)
        {
            Assert.False(HasMethod(stage, method), $"{stage.Name} must not offer {method}.");
        }
    }

    [Fact]
    public void FromZplReturnsTheZplSourceStage()
    {
        using var client = new LabelZoomClient(string.Empty);
        Assert.IsType<ZplSourceBuilder>(client.Convert().FromZpl("^XA^XZ"));
        Assert.IsType<PdfSourceBuilder>(client.Convert().FromPdf(new byte[] { 1 }));
        Assert.IsType<ImageSourceBuilder>(client.Convert().FromPng(new byte[] { 1 }));
        Assert.IsType<SourceBuilder>(client.Convert().FromEpl("N"));
        Assert.IsType<DynamicSourceBuilder>(client.Convert().From(SourceFormat.Zpl, "^XA^XZ"));
    }

    [Fact]
    public async Task TypedChainSendsTheSameParamsAsTheWire()
    {
        var handler = new StubHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, "^XA^XZ", "text/plain");
        using var client = new LabelZoomClient(new LabelZoomClientOptions
        {
            ApiKey = string.Empty,
            HttpMessageHandler = handler,
        });

        await client.Convert()
            .FromPdf(new byte[] { 0x25, 0x50, 0x44, 0x46 })
                .WithPage(0)
                .WithSplit(s => s.Rows(2).Columns(2).SkipBlank())
            .ToZpl()
                .WithDpi(203)
                .WithCollapseCopies()
                .WithLayout(l => l.Across(2).Gap(0.125f))
            .ExecuteAsync();

        var request = handler.LastRequest;
        Assert.Equal("/api/v2/convert/pdf/to/zpl", request.RequestUri.AbsolutePath);

        var json = request.Params!.Value;
        Assert.Equal(0, json.GetProperty("pdf").GetProperty("pageNumber").GetInt32());
        Assert.Equal(2, json.GetProperty("split").GetProperty("rows").GetInt32());
        Assert.Equal(2, json.GetProperty("split").GetProperty("columns").GetInt32());
        Assert.True(json.GetProperty("split").GetProperty("skipBlank").GetBoolean());
        Assert.Equal(203, json.GetProperty("dpi").GetInt32());
        Assert.True(json.GetProperty("zpl").GetProperty("collapseCopies").GetBoolean());
        Assert.Equal(2, json.GetProperty("layout").GetProperty("across").GetInt32());
        Assert.Equal(0.125f, json.GetProperty("layout").GetProperty("gap").GetSingle());
    }

    private static bool HasMethod(Type type, string name) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.Instance).Any(m => m.Name == name);
}
