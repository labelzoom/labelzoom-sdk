namespace LabelZoom.Sdk.Tests;

/// <summary>
/// Pins the numeric value of every <see cref="SourceFormat"/> and <see cref="TargetFormat"/>
/// member.
/// </summary>
/// <remarks>
/// <para>
/// C# compiles an enum member into the calling assembly as a constant. A library built against
/// 1.0.0 that passes <c>TargetFormat.Pdf</c> actually passes <c>6</c>, and keeps passing
/// <c>6</c> against whatever SDK is loaded at run time. Adding <c>Ipl</c> and <c>Sbpl</c>
/// mid-enum once shifted every later member, so <c>6</c> came to mean <c>Xml</c>. Nothing threw,
/// and the conversion was simply wrong.
/// </para>
/// <para>
/// The conformance suite can't catch that because it compiles against the current source. A
/// failure here means an existing value changed. Put it back, and give a new member the next
/// unused number instead.
/// </para>
/// </remarks>
public sealed class FormatValueTests
{
    public static TheoryData<SourceFormat, int> SourceValues => new()
    {
        { SourceFormat.Zpl, 0 },
        { SourceFormat.Epl, 1 },
        { SourceFormat.Tspl, 2 },
        { SourceFormat.Dpl, 3 },
        { SourceFormat.Xml, 4 },
        { SourceFormat.Json, 5 },
        { SourceFormat.Pdf, 6 },
        { SourceFormat.Png, 7 },
        { SourceFormat.Bmp, 8 },
        { SourceFormat.Gif, 9 },
        { SourceFormat.Jpeg, 10 },
        { SourceFormat.Jpg, 11 },
        { SourceFormat.Url, 12 },
        { SourceFormat.Ipl, 13 },
        { SourceFormat.Sbpl, 14 },
    };

    public static TheoryData<TargetFormat, int> TargetValues => new()
    {
        { TargetFormat.Zpl, 0 },
        { TargetFormat.Epl, 1 },
        { TargetFormat.Tspl, 2 },
        { TargetFormat.Dpl, 3 },
        { TargetFormat.Xml, 4 },
        { TargetFormat.Json, 5 },
        { TargetFormat.Pdf, 6 },
        { TargetFormat.Png, 7 },
        { TargetFormat.Bmp, 8 },
        { TargetFormat.Gif, 9 },
        { TargetFormat.Jpeg, 10 },
        { TargetFormat.Ipl, 11 },
        { TargetFormat.Sbpl, 12 },
    };

    [Theory]
    [MemberData(nameof(SourceValues))]
    public void SourceFormatValueIsStable(SourceFormat format, int expected) =>
        Assert.Equal(expected, (int)format);

    [Theory]
    [MemberData(nameof(TargetValues))]
    public void TargetFormatValueIsStable(TargetFormat format, int expected) =>
        Assert.Equal(expected, (int)format);

    // Without this, a new member added with no row above would never be checked.
    [Fact]
    public void EveryMemberIsPinned()
    {
        Assert.Equal(Enum.GetValues<SourceFormat>().Length, SourceValues.Count);
        Assert.Equal(Enum.GetValues<TargetFormat>().Length, TargetValues.Count);
    }
}
