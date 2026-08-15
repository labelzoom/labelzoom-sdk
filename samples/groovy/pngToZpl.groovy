/**
 * pngToZpl.groovy: Uses the LabelZoom API to convert a PNG image to ZPL
 */
def fileName = 'LabelZoom_Logo_f_400px.png' // TODO: Replace this with the relative or absolute path of the image you want to convert

def file = new File(fileName)
def post = new URL('https://api.labelzoom.com/api/v2/convert/png/to/zpl').openConnection();
post.setRequestMethod('POST')
post.setDoOutput(true)
post.setRequestProperty('Content-Type', 'image/png')
post.setRequestProperty('Accept', 'text/plain')
post.getOutputStream().write(file.bytes)
def postRC = post.getResponseCode()
if (!postRC.equals(200)) {
    throw new RuntimeException("API returned status $postRC")
}
def zpl = post.getInputStream().getText()
println(zpl)
