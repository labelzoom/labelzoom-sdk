package com.labelzoom.sdk;

import java.util.LinkedHashMap;
import java.util.Map;

/**
 * The request under construction, handed from the source stage to the target stage.
 *
 * <p>Both stages write into the same parameter map, because the wire has one {@code ?params=}
 * object, not one per stage. The stages only decide which options a caller can reach at each point
 * in the chain.
 */
final class ConversionDraft {

    final LabelZoomClient client;
    final SourceFormat source;
    final byte[] body;
    final String contentType;
    final Map<String, Object> params = new LinkedHashMap<>();
    final Map<String, String> rawQuery = new LinkedHashMap<>();

    /** True when the body is the document base64-encoded as text, which can't be printed. */
    boolean base64Text;

    ConversionDraft(LabelZoomClient client, SourceFormat source, byte[] body, String contentType) {
        this.client = client;
        this.source = source;
        this.body = body;
        this.contentType = contentType;
    }

    /** One group of the wire JSON, such as {@code split}; created on first use and merged after. */
    @SuppressWarnings("unchecked")
    Map<String, Object> nested(String group) {
        return (Map<String, Object>) params.computeIfAbsent(group, k -> new LinkedHashMap<String, Object>());
    }

    /** A copy for one request, so changing the builder afterward can't touch a request in flight. */
    @SuppressWarnings("unchecked")
    Map<String, Object> snapshot() {
        Map<String, Object> copy = new LinkedHashMap<>();
        for (Map.Entry<String, Object> entry : params.entrySet()) {
            Object value = entry.getValue();
            copy.put(entry.getKey(), value instanceof Map
                    ? new LinkedHashMap<>((Map<String, Object>) value)
                    : value);
        }
        return copy;
    }
}
