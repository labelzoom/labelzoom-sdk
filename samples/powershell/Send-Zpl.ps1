<#
.SYNOPSIS
    Sends printer code straight to a network label printer on raw TCP port 9100.

.DESCRIPTION
    Most Zebra printers, and most other network label printers, accept raw printer code on
    port 9100. The file is sent byte for byte, so ZPL, EPL, TSPL and the other printer languages
    all work, including ones that carry binary graphics.

.EXAMPLE
    .\Send-Zpl.ps1 -Path label.zpl -Printer 192.168.0.44

.EXAMPLE
    # Convert a PDF and print it in one line.
    .\Convert-Label.ps1 -Path shipping-label.pdf -From pdf -To zpl -OutFile label.zpl; .\Send-Zpl.ps1 -Path label.zpl -Printer 192.168.0.44
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Path,
    [Parameter(Mandatory)] [string] $Printer,   # IP address or hostname
    [int] $Port = 9100
)

$ErrorActionPreference = 'Stop'

$bytes = [IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $Path).ProviderPath)

$client = New-Object Net.Sockets.TcpClient
try {
    $client.Connect($Printer, $Port)
    $stream = $client.GetStream()
    $stream.Write($bytes, 0, $bytes.Length)
    $stream.Flush()
} finally {
    $client.Close()
}

Write-Host "Sent $($bytes.Length) bytes to ${Printer}:$Port"
