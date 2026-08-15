![LabelZoom Logo](../docs/LabelZoom_Logo_f_400px.png)

# LabelZoom Python Samples

Simple Python scripts for converting images to ZPL and printing ZPL to Zebra printers.

## Features

- **PNG to ZPL Conversion**: Convert PNG images to ZPL format using the LabelZoom API
- **Direct Printer Integration**: Send ZPL directly to Zebra printers over the network
- **Simple & Lightweight**: No complex dependencies, easy to integrate into your projects

## Requirements

- Python 3.6+
- `requests` library (for API calls)

## Installation

Install the required dependencies:

```bash
pip install requests
```

## Available Scripts

### png-to-zpl.py

Converts a PNG image to ZPL format using the LabelZoom API.

**Usage:**

1. Edit the script and set the `image_path` variable to your PNG file:
   ```python
   image_path = 'path/to/your/image.png'
   ```

2. Run the script:
   ```bash
   python png-to-zpl.py
   ```

3. The ZPL output will be printed to the console.

**Example:**

```python
#!/usr/bin/env python
# -*- coding: utf-8 -*-

import requests

image_path = 'LabelZoom_Logo_f_400px.png'

with open(image_path, 'rb') as f:
    image_bytes = f.read()

url = 'https://api.labelzoom.com/api/v2/convert/png/to/zpl'
headers = { 'Content-Type': 'image/png', 'Accept': 'text/plain' }
response = requests.post(url, data=image_bytes, headers=headers)

try:
    zpl = response.text
    print(zpl)
except requests.exceptions.RequestException:
    print(response.text)
```

### print-zpl.py

Sends ZPL code directly to a Zebra printer using a network connection (port 9100).

**Usage:**

1. Edit the script and configure your settings:
   ```python
   zpl = '^XA^FO20,20^A0N,48^FDHello World^FS^XZ'  # Your ZPL code
   printer_ip_or_hostname = '192.168.0.44'  # Your printer's IP or hostname
   ```

2. Run the script:
   ```bash
   python print-zpl.py
   ```

3. The ZPL will be sent to the printer and should print immediately.

**Example:**

```python
#!/usr/bin/env python
# -*- coding: utf-8 -*-

import socket

zpl = '^XA^FO20,20^A0N,48^FDgithub.com/labelzoom^FS^XZ'
printer_ip_or_hostname = '192.168.0.44'

s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
s.connect((printer_ip_or_hostname, 9100))
s.sendall(str.encode(zpl))
s.close()
```

## Complete Workflow Example

Here's how to convert an image to ZPL and print it:

```python
import requests
import socket

# Step 1: Convert PNG to ZPL
image_path = 'my_label.png'
with open(image_path, 'rb') as f:
    image_bytes = f.read()

url = 'https://api.labelzoom.com/api/v2/convert/png/to/zpl'
headers = { 'Content-Type': 'image/png', 'Accept': 'text/plain' }
response = requests.post(url, data=image_bytes, headers=headers)
zpl = response.text

# Step 2: Print to Zebra printer
printer_ip = '192.168.0.44'
s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
s.connect((printer_ip, 9100))
s.sendall(str.encode(zpl))
s.close()

print("Label printed successfully!")
```

## API Endpoints

- **PNG to ZPL**: `POST https://api.labelzoom.com/api/v2/convert/png/to/zpl`
  - Content-Type: `image/png`
  - Accept: `text/plain`

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

