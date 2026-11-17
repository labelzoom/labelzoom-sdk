<#
.SYNOPSIS
    Converts a label file from one format to another with the LabelZoom API.

.DESCRIPTION
    Posts the file to POST https://api.labelzoom.com/api/v2/convert/{From}/to/{To} and writes the
    response bytes to -OutFile. Works in Windows PowerShell 5.1 and PowerShell 7+, with no
    modules to install.

    An API key is optional. Without one you get the free tier: watermarked output, first label
    only, and a 1 MB request cap. The key comes from -ApiKey, else the LABELZOOM_API_KEY
    environment variable.

.EXAMPLE
    .\Convert-Label.ps1 -Path shipping-label.pdf -From pdf -To zpl -OutFile label.zpl

.EXAMPLE
    .\Convert-Label.ps1 -Path label.zpl -From zpl -To png -OutFile label.png -Dpi 300

.EXAMPLE
    # A 4 x 6 in label. Leave -LabelWidth and -LabelHeight out to let the API detect the size.
    .\Convert-Label.ps1 -Path logo.png -From png -To zpl -OutFile logo.zpl -LabelWidth 4 -LabelHeight 6
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Path,

    [Parameter(Mandatory)]
    [ValidateSet('zpl', 'epl', 'ipl', 'tspl', 'dpl', 'sbpl', 'xml', 'json', 'pdf',
                 'png', 'bmp', 'gif', 'jpg', 'jpeg')]
    [string] $From,

    [Parameter(Mandatory)]
    [ValidateSet('zpl', 'epl', 'ipl', 'tspl', 'dpl', 'sbpl', 'xml', 'json', 'pdf',
                 'png', 'bmp', 'gif', 'jpeg')]
    [string] $To,

    [Parameter(Mandatory)] [string] $OutFile,

    [int] $Dpi,
    [float] $LabelWidth,   # inches
    [float] $LabelHeight,  # inches
    [int] $PdfPage = -1,   # 0-based; leave out to convert every page

    [string] $ApiKey = $env:LABELZOOM_API_KEY
)

$ErrorActionPreference = 'Stop'

# Windows PowerShell 5.1 can default to TLS 1.0, which the API refuses.
[Net.ServicePointManager]::SecurityProtocol =
    [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

if ($From -eq 'jpg') { $From = 'jpeg' }

# Send only the options you set. Every option travels in one ?params= JSON object.
$params = @{}
if ($PSBoundParameters.ContainsKey('Dpi')) { $params.dpi = $Dpi }
$label = @{}
if ($PSBoundParameters.ContainsKey('LabelWidth'))  { $label.width  = $LabelWidth }
if ($PSBoundParameters.ContainsKey('LabelHeight')) { $label.height = $LabelHeight }
if ($label.Count -gt 0) { $params.label = $label }
if ($PdfPage -ge 0) { $params.pdf = @{ pageNumber = $PdfPage } }

$uri = "https://api.labelzoom.com/api/v2/convert/$From/to/$To"
if ($params.Count -gt 0) {
    $json = $params | ConvertTo-Json -Compress -Depth 5
    $uri += '?params=' + [Uri]::EscapeDataString($json)
}

$headers = @{ Accept = '*/*' }
if ($ApiKey) { $headers.Authorization = "Bearer $ApiKey" }

# application/octet-stream is accepted for every source format, and the API reads it byte for
# byte, so binary graphics inside printer code survive.
$request = @{
    Uri             = $uri
    Method          = 'Post'
    Headers         = $headers
    ContentType     = 'application/octet-stream'
    InFile          = (Resolve-Path -LiteralPath $Path).ProviderPath
    OutFile         = $OutFile
    UseBasicParsing = $true
}

try {
    Invoke-WebRequest @request
} catch {
    $status = $_.Exception.Response.StatusCode.value__
    $detail = $_.ErrorDetails.Message
    if ($detail -and $detail.TrimStart().StartsWith('{')) {
        $parsed = $detail | ConvertFrom-Json
        if ($parsed.message) { $detail = $parsed.message }
    }
    throw "LabelZoom API returned HTTP ${status}: $detail"
}

Write-Host "Wrote $OutFile"
