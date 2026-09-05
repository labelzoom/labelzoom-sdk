![LabelZoom Logo](../docs/LabelZoom_Logo_f_400px.png)

# LabelZoom Groovy Samples

Groovy scripts for converting images to ZPL and printing ZPL to Zebra printers in JVM-based environments.

## Features

- **PNG to ZPL Conversion**: Convert PNG images to ZPL format using the LabelZoom API
- **Direct Printer Integration**: Send ZPL directly to Zebra printers over the network
- **JVM Compatible**: Works with Groovy, Java, and other JVM languages
- **No External Dependencies**: Uses only standard Java libraries

## Requirements

- Groovy 2.4+ or Java 8+
- No additional dependencies required

## Installation

If you don't have Groovy installed:

### Using SDKMAN! (Recommended)
```bash
sdk install groovy
```

### Manual Installation
Download from [Groovy's official website](https://groovy-lang.org/download.html)

## Available Scripts

### pngToZpl.groovy

Converts a PNG image to ZPL format using the LabelZoom API.

**Usage:**

1. Edit the script and set the `fileName` variable to your PNG file:
   ```groovy
   def fileName = 'path/to/your/image.png'
   ```

2. Run the script:
   ```bash
   groovy pngToZpl.groovy
   ```

3. The ZPL output will be printed to the console.

**Example:**

```groovy
/**
 * pngToZpl.groovy: Uses the LabelZoom API to convert a PNG image to ZPL
 */
def fileName = 'LabelZoom_Logo_f_400px.png'

def file = new File(fileName)
def post = new URL('https://api.labelzoom.com/api/v2/convert/png/to/zpl').openConnection();
post.setRequestMethod('POST')
post.setDoOutput(true)
post.setRequestProperty('Content-Type', 'image/png')
post.setRequestProperty('Accept', '*/*')
post.getOutputStream().write(file.bytes)
def postRC = post.getResponseCode()
if (!postRC.equals(200)) {
    throw new RuntimeException("API returned status $postRC")
}
def zpl = post.getInputStream().getText()
println(zpl)
```

### printZpl.groovy

Sends ZPL code directly to a Zebra printer using a network connection (port 9100).

**Usage:**

1. Edit the script and configure your settings:
   ```groovy
   def zpl = '^XA^FO20,20^A0N,48^FDHello World^FS^XZ'  // Your ZPL code
   def printerIpOrHostname = '192.168.0.44'  // Your printer's IP or hostname
   ```

2. Run the script:
   ```bash
   groovy printZpl.groovy
   ```

3. The ZPL will be sent to the printer and should print immediately.

**Example:**

```groovy
/**
 * printZpl.groovy: Sends ZPL to a Zebra printer using a ZebraNet network adapter
 */
def zpl = '^XA^FO20,20^A0N,48^FDgithub.com/labelzoom^FS^XZ'
def printerIpOrHostname = '192.168.0.44'

try(def socket = new Socket(printerIpOrHostname, 9100); def pw = new PrintWriter(socket.getOutputStream()))
{
    pw.write(zpl)
}
```

## Complete Workflow Example

Here's how to convert an image to ZPL and print it in a single script:

```groovy
// Step 1: Convert PNG to ZPL
def fileName = 'my_label.png'
def file = new File(fileName)
def post = new URL('https://api.labelzoom.com/api/v2/convert/png/to/zpl').openConnection()
post.setRequestMethod('POST')
post.setDoOutput(true)
post.setRequestProperty('Content-Type', 'image/png')
post.setRequestProperty('Accept', '*/*')
post.getOutputStream().write(file.bytes)

if (!post.getResponseCode().equals(200)) {
    throw new RuntimeException("API returned status ${post.getResponseCode()}")
}

def zpl = post.getInputStream().getText()
println("ZPL generated successfully")

// Step 2: Print to Zebra printer
def printerIp = '192.168.0.44'
try(def socket = new Socket(printerIp, 9100); def pw = new PrintWriter(socket.getOutputStream())) {
    pw.write(zpl)
}

println("Label printed successfully!")
```

## API Endpoints

- **PNG to ZPL**: `POST https://api.labelzoom.com/api/v2/convert/png/to/zpl`
  - Content-Type: `image/png`
  - Accept: `*/*`

## Printer Network Configuration

To print directly to a Zebra printer, you need:

1. A Zebra printer with a network adapter (ZebraNet or similar)
2. The printer's IP address or hostname
3. Network connectivity to the printer on port 9100

Most Zebra printers support raw TCP/IP printing on port 9100 by default.

## Authentication

The PNG to ZPL conversion endpoint shown in these samples does not require authentication. For other API endpoints or features, you may need an API token. Contact [LabelZoom](https://www.labelzoom.com) for more information.

## License

This project is licensed under the MIT License - see the [LICENSE](../../LICENSE) file for details.

## Support

For questions or support, visit [LabelZoom](https://www.labelzoom.com) or contact the LabelZoom team.

