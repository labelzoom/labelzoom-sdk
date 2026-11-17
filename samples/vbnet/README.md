![LabelZoom Logo](../../docs/LabelZoom_Logo_f_400px.png)

# LabelZoom VB.NET sample

VB.NET uses the same package as C#: [`LabelZoom.Sdk`](https://www.nuget.org/packages/LabelZoom.Sdk).
It targets .NET Standard 2.0, so it works in .NET Framework 4.6.1 or later as well as in current
.NET. This sample shows the package from VB.NET, and how to send printer code to a network printer.

## Run the sample

You need the [.NET SDK](https://dotnet.microsoft.com/download).

```sh
dotnet run -- convert shipping-label.pdf pdf zpl label.zpl
dotnet run -- convert label.zpl zpl png label.png 300
dotnet run -- print label.zpl 192.168.0.44
```

The optional last argument to `convert` is the output resolution in dpi.

An API key is optional. Without one you get the free tier: watermarked output, first label only,
and a 1 MB request cap. Set the `LABELZOOM_API_KEY` environment variable to use your key.

## Use it in your own project

1. Add the package:

   ```sh
   dotnet add package LabelZoom.Sdk
   ```

   In Visual Studio, you can instead search for `LabelZoom.Sdk` in **Manage NuGet Packages**.

1. Copy `ConvertLabelAsync` and `SendToPrinter` from [`Program.vb`](Program.vb).

The core of a conversion is one chain:

```vb
Using client As New LabelZoomClient()
    Dim result = Await client.Convert() _
        .FromFile(SourceFormat.Pdf, "shipping-label.pdf") _
        .ToZpl() _
        .WithDpi(203) _
        .ExecuteAsync()

    result.Save("label.zpl")
End Using
```

Use `result.Bytes` or `result.Save`, not `result.Text`, for PDF and image output, and for printer
code that might carry graphics.

Errors are typed. Catch `LabelZoomException` to handle all of them, or
`LabelZoomForbiddenException` and check `IsPaidFeature` to detect a request that needs a paid plan.
Every error carries a `RequestId`; quote it if you contact support.

For every option, see the [.NET SDK README](../../dotnet/README.md).

## License

MIT. See [LICENSE](../../LICENSE).
