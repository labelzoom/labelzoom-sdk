![LabelZoom Logo](../docs/LabelZoom_Logo_f_400px.png)

# LabelZoom .NET SDK

A full-featured .NET SDK for converting documents and images to ZPL format using the LabelZoom API.

## Features

- **PDF to ZPL Conversion**: Convert PDF documents to ZPL format
- **Synchronous and Asynchronous APIs**: Choose the best approach for your use case
- **Streaming Support**: Efficiently handle large multi-page documents with streaming callbacks
- **.NET Standard 2.0**: Compatible with .NET Framework 4.6.1+, .NET Core 2.0+, and .NET 5+
- **Cancellation Token Support**: Cancel long-running operations gracefully
- **Configurable Endpoint**: Override the default API endpoint if needed

## Installation

### From Source

```bash
git clone https://github.com/labelzoom/labelzoom-zpl-sdk.git
cd labelzoom-zpl-sdk/dotnet
dotnet build LabelzoomDotnetSdk.sln
```

### NuGet Package

*(Coming soon)*

## Quick Start

### Basic PDF to ZPL Conversion

```csharp
using LabelzoomDotnetSdk;

using (var client = new LabelzoomClient("YOUR_AUTH_TOKEN"))
{
    var zpl = await client.PdfToZpl("path/to/document.pdf");
    Console.WriteLine(zpl);
}
```

### Streaming PDF to ZPL (for large documents)

```csharp
using LabelzoomDotnetSdk;

using (var client = new LabelzoomClient("YOUR_AUTH_TOKEN"))
{
    await client.PdfToZplAsync("path/to/document.pdf", async (zpl) => 
    {
        // Process each label as it's received
        Console.WriteLine($"Received label: {zpl.Substring(0, 50)}...");
        // Send to printer, save to file, etc.
    });
}
```

### Custom Endpoint

```csharp
using LabelzoomDotnetSdk;

using (var client = new LabelzoomClient("YOUR_AUTH_TOKEN"))
{
    client.Endpoint = "https://custom-api.example.com";
    var zpl = await client.PdfToZpl("path/to/document.pdf");
}
```

## API Reference

### LabelzoomClient

#### Constructor

```csharp
public LabelzoomClient(string token)
```

Creates a new instance of the LabelZoom client with the specified authentication token.

#### Properties

- `string Endpoint` - The API endpoint URL (default: `https://api.labelzoom.com`)

#### Methods

##### PdfToZpl

```csharp
public async Task<string> PdfToZpl(string pdfPath, CancellationToken ct = default)
```

Converts a PDF document to a single ZPL string. Best used for smaller documents with fewer pages.

**Parameters:**
- `pdfPath` - Path to the PDF file
- `ct` - Optional cancellation token

**Returns:** ZPL string containing all labels

##### PdfToZplAsync

```csharp
public async Task PdfToZplAsync(string pdfPath, Func<string, Task> onLabelAsync, CancellationToken ct = default)
```

Converts a PDF document to ZPL and streams the response one label at a time. Best used for larger documents with many pages.

**Parameters:**
- `pdfPath` - Path to the PDF file
- `onLabelAsync` - Callback function invoked for each label
- `ct` - Optional cancellation token

## Building

```bash
cd dotnet
dotnet build LabelzoomDotnetSdk.sln
```

## Running Tests

```bash
cd dotnet
dotnet test
```

## Authentication

You need a LabelZoom API token to use this SDK. Contact [LabelZoom](https://www.labelzoom.com) to obtain an API token.

## Code Snippets

Additional code snippets and examples can be found in the `snippets/` directory.

## License

This project is licensed under the MIT License - see the [LICENSE](../LICENSE) file for details.

## Support

For questions or support, visit [LabelZoom](https://www.labelzoom.com) or contact the LabelZoom team.

