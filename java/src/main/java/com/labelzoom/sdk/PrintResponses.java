package com.labelzoom.sdk;

import java.nio.charset.StandardCharsets;
import java.time.Instant;
import java.time.format.DateTimeFormatter;
import java.time.format.DateTimeParseException;
import java.util.Map;

/**
 * Reads print responses. Unknown members are ignored, so the server can add them, and a member of
 * the wrong type reads as absent.
 */
final class PrintResponses {

    private PrintResponses() {
    }

    /** The response body as a JSON object. */
    @SuppressWarnings("unchecked")
    static Map<String, Object> read(HttpTransport.Response response) {
        String body = response.body() == null ? "" : new String(response.body(), StandardCharsets.UTF_8);
        Object root;
        try {
            root = JsonReader.parse(body);
        } catch (IllegalArgumentException e) {
            root = null;
        }
        if (!(root instanceof Map)) {
            throw new LabelZoomException(
                    response.statusCode(),
                    "The LabelZoom API returned a response that isn't a JSON object.",
                    LabelZoomClient.requestId(response), body, null);
        }
        return (Map<String, Object>) root;
    }

    /** A nested object, or an empty map when it is absent. */
    @SuppressWarnings("unchecked")
    static Map<String, Object> object(Map<String, Object> json, String name) {
        Object value = json.get(name);
        return value instanceof Map ? (Map<String, Object>) value : Map.of();
    }

    static String string(Map<String, Object> json, String name) {
        return json.get(name) instanceof String value ? value : null;
    }

    static Integer integer(Map<String, Object> json, String name) {
        Long value = longValue(json, name);
        return value != null && value >= Integer.MIN_VALUE && value <= Integer.MAX_VALUE
                ? Integer.valueOf(value.intValue())
                : null;
    }

    static Long longValue(Map<String, Object> json, String name) {
        Object value = json.get(name);
        if (value instanceof Long whole) {
            return whole;
        }
        if (value instanceof Double d && d == Math.rint(d) && Math.abs(d) < 9.0e15) {
            return Long.valueOf(d.longValue());
        }
        return null;
    }

    static boolean bool(Map<String, Object> json, String name) {
        return Boolean.TRUE.equals(json.get(name));
    }

    /** An ISO 8601 timestamp with an offset, such as {@code 2026-10-11T15:00:00.000Z}. */
    static Instant instant(Map<String, Object> json, String name) {
        String text = string(json, name);
        if (text == null) {
            return null;
        }
        try {
            // ISO_OFFSET_DATE_TIME rather than Instant.parse: before JDK 12, and so on Android,
            // Instant.parse accepts only a trailing Z.
            return DateTimeFormatter.ISO_OFFSET_DATE_TIME.parse(text, Instant::from);
        } catch (DateTimeParseException e) {
            return null;
        }
    }
}
