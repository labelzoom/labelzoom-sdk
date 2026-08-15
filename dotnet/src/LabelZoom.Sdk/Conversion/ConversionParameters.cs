using System;
using System.Collections.Generic;
using System.Text.Json;

namespace LabelZoom.Sdk.Conversion
{
    /// <summary>
    /// Accumulates the conversion options a caller actually set and renders them as the single
    /// <c>?params=</c> JSON object the API expects.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two rules drive the whole design. <b>Only explicitly set options are serialized</b> — an
    /// unset value is absent from the JSON entirely, never sent as a client-side default, so a
    /// future change to a server default reaches callers who never overrode it.
    /// </para>
    /// <para>
    /// <b>Everything travels in one <c>?params=</c> JSON object</b>, never in dot-notation query
    /// parameters. The API accepts both and merges them, but dot-notation cannot express every
    /// parameter — <c>?data=[{}]</c> is rejected with 400 while
    /// <c>?params={"data":[{}]}</c> succeeds. One serialization path with no per-field special
    /// cases is the only maintainable option.
    /// </para>
    /// </remarks>
    internal sealed class ConversionParameters
    {
        private readonly Dictionary<string, object?> _root = new Dictionary<string, object?>();
        private readonly Dictionary<string, string> _rawQuery = new Dictionary<string, string>();

        internal bool IsEmpty => _root.Count == 0;

        internal IReadOnlyDictionary<string, string> RawQueryParameters => _rawQuery;

        internal ConversionParameters Clone()
        {
            var copy = new ConversionParameters();
            foreach (var pair in _root)
            {
                // Nested groups are mutable, so copy them rather than aliasing.
                copy._root[pair.Key] = pair.Value is Dictionary<string, object?> nested
                    ? new Dictionary<string, object?>(nested)
                    : pair.Value;
            }

            foreach (var pair in _rawQuery)
            {
                copy._rawQuery[pair.Key] = pair.Value;
            }

            return copy;
        }

        internal void Set(string key, object? value) => _root[key] = value;

        internal void SetNested(string group, string key, object? value)
        {
            if (!_root.TryGetValue(group, out var existing) ||
                existing is not Dictionary<string, object?> nested)
            {
                nested = new Dictionary<string, object?>();
                _root[group] = nested;
            }

            nested[key] = value;
        }

        internal void SetRawQueryParameter(string key, string value) => _rawQuery[key] = value;

        /// <summary>
        /// Serializes to the JSON string that becomes the <c>params</c> query value, or
        /// <c>null</c> when nothing was set — in which case no query parameter is emitted at all.
        /// </summary>
        internal string? ToJson() =>
            IsEmpty ? null : JsonSerializer.Serialize(_root, SerializerOptions);

        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            // The accumulator only ever holds values the caller set, so nulls here would be
            // meaningful. There are none today; this guards a future regression.
            WriteIndented = false,
        };

        /// <summary>
        /// Validates and normalizes a caller-supplied data record set into JSON objects.
        /// </summary>
        /// <remarks>
        /// <c>data</c> is always an array — one entry produces one label. A caller passing a single
        /// record means "one label", so a bare object is wrapped rather than rejected.
        /// </remarks>
        internal static List<JsonElement> NormalizeData(IEnumerable<object?> records)
        {
            var normalized = new List<JsonElement>();
            var index = 0;

            foreach (var record in records)
            {
                if (record is null)
                {
                    throw new LabelZoomValidationException(
                        "data", $"data[{index}] is null; every entry must be a JSON object.");
                }

                JsonElement element;
                try
                {
                    // Round-tripping through a document is the portable way to both normalize an
                    // arbitrary POCO and inspect the result's JSON kind. Clone() detaches the
                    // element from the document being disposed.
                    using var document = JsonDocument.Parse(JsonSerializer.Serialize(record));
                    element = document.RootElement.Clone();
                }
                catch (JsonException ex)
                {
                    throw new LabelZoomValidationException(
                        "data", $"data[{index}] could not be serialized to JSON: {ex.Message}");
                }

                if (element.ValueKind != JsonValueKind.Object)
                {
                    throw new LabelZoomValidationException(
                        "data",
                        $"data[{index}] is a JSON {element.ValueKind.ToString().ToLowerInvariant()}; " +
                        "every entry must be an object whose keys are the label's variable field names.");
                }

                normalized.Add(element);
                index++;
            }

            return normalized;
        }
    }
}
