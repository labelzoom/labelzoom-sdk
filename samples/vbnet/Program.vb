' Converts a label with the LabelZoom .NET SDK, and sends printer code to a network printer.
'
'   dotnet run -- convert shipping-label.pdf pdf zpl label.zpl
'   dotnet run -- convert label.zpl zpl png label.png 300
'   dotnet run -- print label.zpl 192.168.0.44
'
' An API key is optional. Without one you get the free tier: watermarked output, first label
' only, and a 1 MB request cap. Set LABELZOOM_API_KEY to use your key.

Imports System.IO
Imports System.Net.Sockets
Imports System.Threading.Tasks
Imports LabelZoom.Sdk

Module Program

    Function Main(args As String()) As Integer
        If args.Length >= 5 AndAlso args(0) = "convert" Then
            Dim dpi As Integer? = Nothing
            If args.Length >= 6 Then dpi = Integer.Parse(args(5))
            Return ConvertLabelAsync(args(1), args(2), args(3), args(4), dpi).GetAwaiter().GetResult()
        End If

        If args.Length >= 3 AndAlso args(0) = "print" Then
            SendToPrinter(args(1), args(2))
            Return 0
        End If

        Console.Error.WriteLine("usage: convert <input> <from> <to> <output> [dpi]")
        Console.Error.WriteLine("       print <file> <printer-ip-or-hostname>")
        Return 2
    End Function

    ''' <summary>Converts <paramref name="inputPath"/> and saves the result to <paramref name="outputPath"/>.</summary>
    ''' <param name="from">Source format, for example "pdf", "png" or "zpl".</param>
    ''' <param name="to">Target format, for example "zpl", "png" or "pdf".</param>
    ''' <param name="dpi">Output resolution, or Nothing to use the API default.</param>
    Async Function ConvertLabelAsync(inputPath As String, from As String, [to] As String,
                                     outputPath As String, dpi As Integer?) As Task(Of Integer)
        Dim source = CType([Enum].Parse(GetType(SourceFormat), from, ignoreCase:=True), SourceFormat)
        Dim target = CType([Enum].Parse(GetType(TargetFormat), [to], ignoreCase:=True), TargetFormat)

        ' Reads LABELZOOM_API_KEY. Pass a key to the constructor to set one explicitly.
        Using client As New LabelZoomClient()
            Try
                Dim request = client.Convert().FromFile(source, inputPath).To(target)
                If dpi.HasValue Then request = request.WithDpi(dpi.Value)

                Dim result = Await request.ExecuteAsync()

                ' Save writes the raw bytes. Use them, not result.Text, for PDF and image output,
                ' and for printer code that might carry graphics.
                result.Save(outputPath)
                Console.WriteLine($"Wrote {outputPath} ({result.Bytes.Length} bytes)")
                Return 0

            Catch ex As LabelZoomForbiddenException When ex.IsPaidFeature
                Console.Error.WriteLine($"Needs a paid plan: {ex.Message}")
                Return 1

            Catch ex As LabelZoomException
                ' Quote the request ID if you contact LabelZoom support.
                Console.Error.WriteLine($"HTTP {CInt(ex.StatusCode)}: {ex.Message} (request {ex.RequestId})")
                Return 1
            End Try
        End Using
    End Function

    ''' <summary>
    ''' Sends a file to a network label printer on raw TCP port 9100, byte for byte, so binary
    ''' graphics inside the printer code survive.
    ''' </summary>
    Sub SendToPrinter(path As String, printer As String, Optional port As Integer = 9100)
        Dim bytes = File.ReadAllBytes(path)
        Using tcp As New TcpClient(printer, port)
            Using stream = tcp.GetStream()
                stream.Write(bytes, 0, bytes.Length)
            End Using
        End Using
        Console.WriteLine($"Sent {bytes.Length} bytes to {printer}:{port}")
    End Sub

End Module
