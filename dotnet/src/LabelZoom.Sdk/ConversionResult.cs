using System;
using System.Text;

namespace LabelZoom.Sdk
{
    /// <summary>
    /// The outcome of a successful conversion.
    /// </summary>
    /// <remarks>
    /// <see cref="Bytes"/> is authoritative. PDF, PNG, BMP, GIF and JPEG targets are binary, so an
    /// API that returned only a string would silently corrupt five of the eight targets.
    /// </remarks>
    public sealed class ConversionResult
    {
        private readonly Encoding _encoding;
        private string? _text;

        internal ConversionResult(
            byte[] bytes, string? contentType, int statusCode, string? requestId, Encoding encoding)
        {
            Bytes = bytes;
            ContentType = contentType;
            StatusCode = statusCode;
            RequestId = requestId;
            _encoding = encoding;
        }

        /// <summary>The converted document, exactly as the server returned it.</summary>
        public byte[] Bytes { get; }

        /// <summary>The response <c>Content-Type</c>, including any charset parameter.</summary>
        public string? ContentType { get; }

        /// <summary>The HTTP status code, always 2xx here.</summary>
        public int StatusCode { get; }

        /// <summary>
        /// The value of the <c>X-LZ-Request-Id</c> response header, or <c>null</c> if the server
        /// did not send one. Quote it when contacting LabelZoom support.
        /// </summary>
        public string? RequestId { get; }

        /// <summary>
        /// <see cref="Bytes"/> decoded using the response charset, defaulting to UTF-8.
        /// </summary>
        /// <remarks>
        /// Meaningful for the ZPL, XML and JSON targets. Decoding a PNG will succeed and produce
        /// nonsense — use <see cref="Bytes"/> for binary targets.
        /// </remarks>
        public string Text => _text ??= _encoding.GetString(Bytes, 0, Bytes.Length);

        /// <summary>Writes <see cref="Bytes"/> to a file.</summary>
        /// <param name="path">Destination path. An existing file is overwritten.</param>
        public void Save(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Path cannot be null or empty.", nameof(path));
            }

            System.IO.File.WriteAllBytes(path, Bytes);
        }
    }
}
