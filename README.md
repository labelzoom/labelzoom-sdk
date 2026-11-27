![LabelZoom Logo](docs/LabelZoom_Logo_f_400px.png)

# LabelZoom SDK

Official client libraries for the [LabelZoom API](https://api.labelzoom.com), in eight languages.
Convert barcode labels between printer languages, PDF, images, and design files, and print them on
any printer connected through the LabelZoom Print Agent.

The SDKs call the same conversion pipeline that serves labelzoom.com. Every SDK reads ZPL, EPL,
IPL, TSPL, DPL, SBPL, CPCL, ESC/Label, Fingerprint, EZPL, and TPCL printer code; NiceLabel,
BarTender, and DYMO label designs; PDF; PNG, BMP, GIF, and JPEG images; and LabelZoom XML and
JSON. It writes ZPL, EPL, IPL, TSPL, DPL, SBPL, and CPCL printer code; PDF; images; and LabelZoom
XML and JSON.

## Packages

| Language | Package | Version |
| --- | --- | --- |
| [.NET](dotnet/) | [`LabelZoom.Sdk`](https://www.nuget.org/packages/LabelZoom.Sdk) | [![NuGet](https://img.shields.io/nuget/v/LabelZoom.Sdk)](https://www.nuget.org/packages/LabelZoom.Sdk) |
| [Node and TypeScript](node/) | [`@labelzoom/sdk`](https://www.npmjs.com/package/@labelzoom/sdk) | [![npm](https://img.shields.io/npm/v/@labelzoom/sdk)](https://www.npmjs.com/package/@labelzoom/sdk) |
| [Java](java/) | [`com.labelzoom:labelzoom-sdk`](https://central.sonatype.com/artifact/com.labelzoom/labelzoom-sdk) | [![Maven Central](https://img.shields.io/maven-central/v/com.labelzoom/labelzoom-sdk)](https://central.sonatype.com/artifact/com.labelzoom/labelzoom-sdk) |
| [Python](python/) | [`labelzoom-sdk`](https://pypi.org/project/labelzoom-sdk/) | [![PyPI](https://img.shields.io/pypi/v/labelzoom-sdk)](https://pypi.org/project/labelzoom-sdk/) |
| [PHP](php/) | [`labelzoom/sdk`](https://packagist.org/packages/labelzoom/sdk) | [![Packagist](https://img.shields.io/packagist/v/labelzoom/sdk)](https://packagist.org/packages/labelzoom/sdk) |
| [Go](go/) | [`github.com/labelzoom/labelzoom-sdk/go`](https://pkg.go.dev/github.com/labelzoom/labelzoom-sdk/go) | [![Go](https://img.shields.io/github/v/tag/labelzoom/labelzoom-sdk?filter=go%2F*&label=go)](https://pkg.go.dev/github.com/labelzoom/labelzoom-sdk/go) |
| [Ruby](ruby/) | [`labelzoom`](https://rubygems.org/gems/labelzoom) | [![Gem](https://img.shields.io/gem/v/labelzoom)](https://rubygems.org/gems/labelzoom) |
| [Rust](rust/) | [`labelzoom`](https://crates.io/crates/labelzoom) | [![crates.io](https://img.shields.io/crates/v/labelzoom)](https://crates.io/crates/labelzoom) |

Each language directory has a README with install steps and examples. For languages without a
package, such as PowerShell, Groovy, and VB.NET, copy a snippet from [`samples/`](samples/).

## Convert a label

A conversion is a pipeline: choose the source, say how to read it, choose the target, say what to
write, then run it. The examples here are C#; every SDK has the same steps in its own idiom.

```csharp
using var client = new LabelZoomClient();          // reads LABELZOOM_API_KEY; works without one

var result = await client.Convert()
    .FromPdf(pdf)                                   // source
        .WithPage(0)                                //   how it's read: 0-based page
        .WithLabelSize(widthInches: 4f, heightInches: 6f)
    .ToZpl()                                        // target
        .WithDpi(203)                               //   what's written: for a 203 dpi printer
    .ExecuteAsync();

File.WriteAllBytes("label.zpl", result.Bytes);
```

The chain only offers an option where it acts. `FromZpl(zpl).WithPage(0)` doesn't compile, because
ZPL has no pages, and neither does `ToPdf().WithDpi(300)`, because a PDF has no resolution.

## Print a label

Printing is the same pipeline with a printer as the target. The printer's own configuration sets its
language, and LabelZoom converts the document to it, so a PDF or a BarTender file prints on a ZPL
printer. Printing needs an API key with the `print` scope.

```csharp
var job = await client.Convert()
    .FromPdf(pdf).WithPage(0)
    .ToPrinter(printerId)
        .WithCopies(2)
        .WithIdempotencyKey(orderNumber)            // a repeat returns this job instead of printing again
    .ExecuteAsync();

var status = await client.Jobs.GetAsync(job.JobId); // queued, dispatched, printing, completed, failed
```

Templates published from LabelZoom Studio or the web designer print the same way:
`Convert().FromTemplate(templateId).WithData(order).ToPrinter(printerId)`.

## What the SDK handles for you

| You get | So you don't have to |
| --- | --- |
| Retries on `429`, `5xx`, and dropped connections, with backoff that honors `Retry-After` | Write a retry loop, or guess how long a rate limit lasts |
| An `Idempotency-Key` on every print, kept the same across retries | Print a label twice when a response is lost and the request is retried |
| Typed errors with the server's message, error `code`, and request ID | Parse error bodies, or match on message text |
| Bytes for binary output, and text decoded with the response's charset | Corrupt a PNG, a PDF, or a printer job with embedded graphics by reading it as a string |
| Validation of options before the request is sent | Spend a round trip to learn that a rotation must be a multiple of 90 |
| Only the options you set, sent in one `params` object | Override the server's own defaults with an SDK's guesses |
| A client that works with no API key | Sign up before you can try it. Output has a watermark until you add a key. |

Without an API key you get the free tier: output has a watermark, and one label converts per
request. See [Rate limits and plans](https://docs.labelzoom.com/reference/rate-limits-and-plans/)
on docs.labelzoom.com.

## Why a conformance suite

Eight independent implementations of one protocol drift, and the drift is quiet: a wrong request is
often still a valid one. So the behavior is written down once and tested the same way everywhere.

- [`docs/API_CONTRACT.md`](docs/API_CONTRACT.md) states each rule, with the reason it exists.
- [`conformance/`](conformance/) holds the testable rules as language-neutral JSON cases: a call and
  the exact request it must send, or a response and the result or error it must produce.
- Every language's test suite runs every case, and fails if it skipped one without a written reason.

Each rule is there because a client got it wrong. Two examples:

- **Only options the caller set are sent.** The output follows the source's resolution unless you
  name another. An SDK that sends `targetDpi=203` by default turns a label written for a 300 dpi
  printer into a 203 dpi one, with no error.
- **One `Idempotency-Key` per print call, reused on every retry.** A new key per attempt makes the
  retry a second print. A missing key does too.

## Repository layout

| Path | What it holds |
| --- | --- |
| `docs/API_CONTRACT.md` | What every SDK must do |
| `docs/CONFORMANCE.md` | How the fixtures work, and how to add a language |
| `conformance/` | The language-neutral cases every SDK runs |
| `dotnet/`, `node/`, `java/`, `python/`, `php/`, `go/`, `ruby/`, `rust/` | One SDK per directory |
| `samples/` | Copy-paste snippets for languages without a package |

## Contributing

1. Read [`docs/API_CONTRACT.md`](docs/API_CONTRACT.md), then [`docs/CONFORMANCE.md`](docs/CONFORMANCE.md).
2. To change behavior, change the contract and its fixture first. A fixture change runs every
   language's suite.
3. Run the fixture linter before you open a pull request:

   ```sh
   node conformance/lint.mjs
   ```

npm, PyPI, NuGet, crates.io, and RubyGems releases publish from CI through OIDC trusted publishing,
with no stored credential. Maven Central and Packagist have no OIDC equivalent, so they use scoped
repository secrets. Packagist reads `composer.json` from a repository root, so the PHP SDK publishes
through a mirror repository; [`release-php.yml`](.github/workflows/release-php.yml) explains why. Go
has no registry: the module proxy serves each `go/vX.Y.Z` tag directly.

## License

MIT. See [LICENSE](LICENSE).

## Links

- [LabelZoom](https://www.labelzoom.com)
- [API documentation](https://docs.labelzoom.com)
- [OpenAPI spec](https://api.labelzoom.com/v3/api-docs) and [Swagger UI](https://api.labelzoom.com/swagger-ui/)
