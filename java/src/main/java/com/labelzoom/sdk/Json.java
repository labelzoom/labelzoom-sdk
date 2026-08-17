package com.labelzoom.sdk;

import java.util.List;
import java.util.Map;

/**
 * The minimum JSON writer needed to build the {@code params} query value.
 *
 * <p>Hand-rolled so the published artifact has <b>zero runtime dependencies</b>. That matters more
 * than usual here: this SDK is dropped into WMS, ERP and TMS deployments whose classpaths are
 * already crowded, and a transitive Jackson or Gson is a genuine source of version conflicts.
 *
 * <p>Only what the API accepts is supported — objects, arrays, strings, numbers, booleans and null.
 */
final class Json {

    private Json() {
    }

    static String write(Object value) {
        StringBuilder out = new StringBuilder();
        writeValue(out, value);
        return out.toString();
    }

    private static void writeValue(StringBuilder out, Object value) {
        if (value == null) {
            out.append("null");
        } else if (value instanceof String s) {
            writeString(out, s);
        } else if (value instanceof Boolean b) {
            out.append(b.booleanValue());
        } else if (value instanceof Number n) {
            writeNumber(out, n);
        } else if (value instanceof Map<?, ?> map) {
            writeObject(out, map);
        } else if (value instanceof Iterable<?> iterable) {
            writeArray(out, iterable);
        } else if (value instanceof Object[] array) {
            writeArray(out, List.of(array));
        } else {
            throw new LabelZoomValidationException(
                    "params",
                    "Cannot serialize " + value.getClass().getName() + " to JSON. Conversion "
                            + "parameters accept maps, lists, strings, numbers, booleans and null.");
        }
    }

    private static void writeObject(StringBuilder out, Map<?, ?> map) {
        out.append('{');
        boolean first = true;
        for (Map.Entry<?, ?> entry : map.entrySet()) {
            if (!first) {
                out.append(',');
            }
            first = false;
            writeString(out, String.valueOf(entry.getKey()));
            out.append(':');
            writeValue(out, entry.getValue());
        }
        out.append('}');
    }

    private static void writeArray(StringBuilder out, Iterable<?> values) {
        out.append('[');
        boolean first = true;
        for (Object value : values) {
            if (!first) {
                out.append(',');
            }
            first = false;
            writeValue(out, value);
        }
        out.append(']');
    }

    private static void writeNumber(StringBuilder out, Number number) {
        double d = number.doubleValue();
        if (Double.isNaN(d) || Double.isInfinite(d)) {
            throw new LabelZoomValidationException(
                    "params", "JSON has no representation for " + number + ".");
        }
        // Render whole-valued floats as integers so `label.width` reads 4 rather than 4.0. The
        // server accepts either; this keeps the request readable in logs and support tickets.
        if ((number instanceof Float || number instanceof Double) && d == Math.rint(d)
                && Math.abs(d) < 1e15) {
            out.append((long) d);
        } else {
            out.append(number);
        }
    }

    private static void writeString(StringBuilder out, String value) {
        out.append('"');
        for (int i = 0; i < value.length(); i++) {
            char c = value.charAt(i);
            switch (c) {
                case '"' -> out.append("\\\"");
                case '\\' -> out.append("\\\\");
                case '\n' -> out.append("\\n");
                case '\r' -> out.append("\\r");
                case '\t' -> out.append("\\t");
                case '\b' -> out.append("\\b");
                case '\f' -> out.append("\\f");
                default -> {
                    if (c < 0x20) {
                        out.append(String.format("\\u%04x", (int) c));
                    } else {
                        out.append(c);
                    }
                }
            }
        }
        out.append('"');
    }
}
