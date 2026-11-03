![LabelZoom Logo](../../docs/LabelZoom_Logo_f_400px.png)

# LabelZoom PowerShell samples

Two scripts that convert labels with the LabelZoom API and send printer code to a network printer.
They run in Windows PowerShell 5.1 and PowerShell 7 or later, with no modules to install.

| Script | What it does |
|---|---|
| [`Convert-Label.ps1`](Convert-Label.ps1) | Converts a file between any two formats the API supports, for example PDF to ZPL or ZPL to PNG |
| [`Send-Zpl.ps1`](Send-Zpl.ps1) | Sends printer code to a network printer on raw TCP port 9100 |

## Convert a label

```powershell
.\Convert-Label.ps1 -Path shipping-label.pdf -From pdf -To zpl -OutFile label.zpl
.\Convert-Label.ps1 -Path label.zpl -From zpl -To png -OutFile label.png -Dpi 300
```

| Parameter | Required | Description |
|---|---|---|
| `-Path` | Yes | The file to convert |
| `-From` | Yes | Source format: `zpl` `epl` `ipl` `tspl` `dpl` `sbpl` `xml` `json` `pdf` `png` `bmp` `gif` `jpg` `jpeg` |
| `-To` | Yes | Target format: `zpl` `epl` `ipl` `tspl` `dpl` `sbpl` `xml` `json` `pdf` `png` `bmp` `gif` `jpeg` |
| `-OutFile` | Yes | Where to write the result. The script writes the raw bytes, so PDF and image output is safe |
| `-Dpi` | No | Output resolution. The API default is 203 |
| `-LabelWidth`, `-LabelHeight` | No | Label size in inches. Leave them out and the API uses the size the printer code declares, or detects it from the content |
| `-PdfPage` | No | Converts one PDF page. Pages are numbered from 0. Leave it out to convert every page |
| `-ApiKey` | No | Your API key. Defaults to the `LABELZOOM_API_KEY` environment variable |

An API key is optional. Without one you get the free tier: watermarked output, first label only,
and a 1 MB request cap. A request that needs a paid plan fails with a message such as
`HTTP 403: JSON export is a paid feature`.

If Windows blocks the script, run it with
`powershell -ExecutionPolicy Bypass -File .\Convert-Label.ps1 ...`, or unblock the file with
`Unblock-File .\Convert-Label.ps1`.

## Print a label

```powershell
.\Send-Zpl.ps1 -Path label.zpl -Printer 192.168.0.44
```

The printer needs a network connection and must accept raw printing on port 9100. Most Zebra
printers do by default. The file is sent byte for byte, so EPL, TSPL and the other printer
languages work too. Use `-Port` if your printer listens on a different port.

## Convert and print in one step

```powershell
.\Convert-Label.ps1 -Path shipping-label.pdf -From pdf -To zpl -OutFile label.zpl
.\Send-Zpl.ps1 -Path label.zpl -Printer 192.168.0.44
```

## License

MIT. See [LICENSE](../../LICENSE).
