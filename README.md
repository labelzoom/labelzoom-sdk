![LabelZoom Logo](docs/LabelZoom_Logo_f_400px.png)

# LabelZoom SDK

Official client libraries for the [LabelZoom API](https://api.labelzoom.com) — convert barcode
labels between ZPL, EPL, TSPL, DPL, PDF, LabelZoom XML/JSON, and raster images.

## Status

The SDKs are being rebuilt around a shared, machine-checked
[API contract](docs/API_CONTRACT.md). This table is the honest state of play, not a roadmap:

| Language | Package | Status |
|---|---|---|
| [.NET](dotnet/) | [`LabelZoom.Sdk`](https://www.nuget.org/packages/LabelZoom.Sdk) | released — `1.0.0`, the reference implementation |
| [Node / TypeScript](node/) | [`@labelzoom/sdk`](https://www.npmjs.com/package/@labelzoom/sdk) | released — `1.0.0` |
| [Java](java/) | [`com.labelzoom:labelzoom-sdk`](https://central.sonatype.com/artifact/com.labelzoom/labelzoom-sdk) | released — `1.0.0` |
| [Python](python/) | [`labelzoom-sdk`](https://pypi.org/project/labelzoom-sdk/) | conformance-green — `1.0.0` release pending |
| Go | `github.com/labelzoom/labelzoom-sdk/go` | planned |
| [PHP](php/) | [`labelzoom/sdk`](https://packagist.org/packages/labelzoom/sdk) | conformance-green — `1.0.0` release pending |
| Ruby | `labelzoom` | planned |

npm, PyPI and NuGet release from CI over OIDC trusted publishing, with no stored
credential. Maven Central and Packagist have no OIDC equivalent, so those two use scoped
repository secrets — and Packagist, which reads `composer.json` from a repository root
rather than accepting an upload, publishes via a split mirror repo
([`.github/workflows/release-php.yml`](.github/workflows/release-php.yml) explains why).
They ship at `1.0.0` now that five independent implementations have validated the shared
contract against the same fixtures; Go and Ruby will join at `1.x` rather than hold the other
five back. The contract carries its own version (`conformance/spec.json`), currently `1.1.0` —
see [`docs/API_CONTRACT.md`](docs/API_CONTRACT.md).

For copy-paste snippets in languages without a package — PowerShell, Groovy, VB.NET, and
friends — see [`samples/`](samples/).

## What the API does

One endpoint covers almost everything:

```
POST https://api.labelzoom.com/api/v2/convert/{sourceFormat}/to/{targetFormat}
```

**Sources (12, plus `url`):** `zpl` `epl` `tspl` `dpl` `xml` `json` `pdf` `png` `bmp` `gif` `jpg` `jpeg`

**Targets (11):** `zpl` `epl` `tspl` `dpl` `xml` `json` `pdf` `png` `bmp` `gif` `jpeg`

`jpg` and `url` are **source-only** — `jpg` is an input spelling that normalizes to `jpeg`, and
`url` tells the server to go fetch a document rather than naming a format. Every SDK enforces that
at compile time where the language allows it.

The printer languages round-trip: `epl`, `tspl` and `dpl` are targets as well as sources, so
`pdf/to/epl` and `zpl/to/tspl` are real conversions. Their output is `text/plain` with every label
concatenated — but EPL's `GW` and TSPL's `BITMAP` commands inline raw binary, so read the result's
**bytes**, not its text, whenever a label might carry graphics.

**Authentication is optional.** Without a key you get the free tier: watermarked output, first
label only, a 1 MB request cap, and no multi-page, JSON-target, or image-to-image conversion. With
a key you get the rest. Every SDK works with no credential configured — that is a contract
requirement, not an accident.

```csharp
// No API key: this works, and returns a watermarked label.
using var client = new LabelZoomClient();

var result = await client.Convert()
    .FromZpl("^XA^FO20,20^A0N,28^FDHello^FS^XZ")
    .ToPng()
    .WithDpi(300)
    .WithLabelSize(widthInches: 4f, heightInches: 6f)
    .ExecuteAsync();

await File.WriteAllBytesAsync("label.png", result.Bytes);
```

Set `LABELZOOM_API_KEY` and every SDK picks it up automatically.

## Repository layout

```
docs/API_CONTRACT.md   what every SDK must do, stated once
docs/CONFORMANCE.md    how to add a language
conformance/           language-neutral fixtures all SDKs are tested against
dotnet/ node/ java/ …  one directory per SDK
samples/               copy-paste snippets for languages without a package
```

## Why a conformance suite

Seven independent implementations of one wire protocol drift, and they drift quietly. So the
behavior lives in [`docs/API_CONTRACT.md`](docs/API_CONTRACT.md) as numbered rules, the
machine-checkable subset lives in [`conformance/`](conformance/) as language-neutral JSON, and
every language's test suite runs the same cases and asserts it ran all of them.

The rules exist because each one was gotten wrong at least once. Two examples:

- **`Accept: */*`, always.** The server's `produces` list omits `image/gif`, `image/bmp`, and
  `image/jpeg`, so sending the target's exact media type returns **406** for those three targets.
- **`pdf.pageNumber` is 0-based** and `label.width` / `label.height` are **inches**. Both are
  routinely misread; both are pinned by fixtures.

```sh
node conformance/lint.mjs
```

## Contributing

Read [`docs/API_CONTRACT.md`](docs/API_CONTRACT.md) first, then
[`docs/CONFORMANCE.md`](docs/CONFORMANCE.md). Changing SDK behavior means changing a fixture, and
changing a fixture re-runs every language.

## License

MIT — see [LICENSE](LICENSE).

## Links

- [LabelZoom](https://www.labelzoom.com)
- [API documentation](https://docs.labelzoom.com)
- [OpenAPI spec](https://api.labelzoom.com/v3/api-docs) · [Swagger UI](https://api.labelzoom.com/swagger-ui/)
