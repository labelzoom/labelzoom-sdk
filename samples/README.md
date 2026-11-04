![LabelZoom Logo](../docs/LabelZoom_Logo_f_400px.png)

# LabelZoom samples

Copy-paste code for calling the LabelZoom API from languages that don't have their own package.

| Language | Sample | What it shows |
|---|---|---|
| PowerShell | [`powershell/`](powershell/) | Convert between any two formats, and print to a network printer. Windows PowerShell 5.1 and PowerShell 7+ |
| VB.NET | [`vbnet/`](vbnet/) | The .NET package from VB.NET, and printing to a network printer |
| Groovy | [`groovy/`](groovy/) | PNG to ZPL with the JDK's HTTP client, and printing to a network printer |

## Languages with a package

Use the package rather than a sample. It handles retries, typed errors and binary output for you.

| Language | Package |
|---|---|
| C# and other .NET languages | [`LabelZoom.Sdk`](../dotnet/) |
| JavaScript and TypeScript | [`@labelzoom/sdk`](../node/) |
| Java and other JVM languages | [`com.labelzoom:labelzoom-sdk`](../java/) |
| Python | [`labelzoom-sdk`](../python/) |
| PHP | [`labelzoom/sdk`](../php/) |
| Go | [`github.com/labelzoom/labelzoom-sdk/go`](../go/) |
| Ruby | [`labelzoom`](../ruby/) |
| Rust | [`labelzoom`](../rust/) |

## Rules every sample follows

These come from the [API contract](../docs/API_CONTRACT.md):

- `POST https://api.labelzoom.com/api/v2/convert/{source}/to/{target}`
- `Accept: */*` for every target
- Options go in one `?params=` JSON object, and only the options you set are sent
- No label size is sent unless you set one, so the API can use the size the printer code
  declares or detect it from the content
- An API key is optional, and the `Authorization` header is sent only when you have one
