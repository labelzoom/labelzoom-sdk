/**
 * printZpl.groovy: Sends ZPL to a Zebra printer using a ZebraNet network adapter
 */
def zpl = '^XA^FO20,20^A0N,48^FDgithub.com/labelzoom^FS^XZ' // TODO: Replace this with the ZPL you want to print
def printerIpOrHostname = '192.168.0.44' // TODO: Replace this with the IP address or hostname of the printer

try(def socket = new Socket(printerIpOrHostname, 9100); def pw = new PrintWriter(socket.getOutputStream()))
{
    pw.write(zpl)
}
