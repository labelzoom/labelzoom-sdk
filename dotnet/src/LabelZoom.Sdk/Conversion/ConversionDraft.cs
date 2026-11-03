namespace LabelZoom.Sdk.Conversion
{
    /// <summary>
    /// The request under construction, handed from the source stage to the target stage.
    /// </summary>
    /// <remarks>
    /// Both stages write into the same <see cref="ConversionParameters"/>, because the wire has one
    /// <c>?params=</c> object, not one per stage. The stages only decide which options a caller can
    /// reach at each point in the chain.
    /// </remarks>
    internal sealed class ConversionDraft
    {
        internal ConversionDraft(
            LabelZoomClient client, SourceFormat source, byte[] body, string contentType)
        {
            Client = client;
            Source = source;
            Body = body;
            ContentType = contentType;
        }

        internal LabelZoomClient Client { get; }

        internal SourceFormat Source { get; }

        internal byte[] Body { get; }

        internal string ContentType { get; }

        internal ConversionParameters Parameters { get; } = new ConversionParameters();
    }
}
