package com.labelzoom.sdk;

import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * The minimum JSON parser needed to read print responses and an error body's {@code code}.
 *
 * <p>Hand-rolled for the same reason as {@link Json}: the published artifact has zero runtime
 * dependencies. It yields {@link Map} (insertion-ordered), {@link List}, {@link String},
 * {@link Long} for whole numbers that fit, {@link Double} for every other number, {@link Boolean}
 * and {@code null}. Malformed input throws {@link IllegalArgumentException}.
 */
final class JsonReader {

    private final String text;
    private int index;

    private JsonReader(String text) {
        this.text = text;
    }

    static Object parse(String text) {
        JsonReader reader = new JsonReader(text);
        reader.skipWhitespace();
        Object value = reader.readValue();
        reader.skipWhitespace();
        if (reader.index != text.length()) {
            throw reader.error("Unexpected trailing content");
        }
        return value;
    }

    /** The top-level string member {@code name} of a JSON object, or null for anything else. */
    static String topLevelString(String text, String name) {
        if (text == null || Compat.isBlank(text)) {
            return null;
        }
        try {
            Object root = parse(text);
            if (root instanceof Map<?, ?> map && map.get(name) instanceof String value) {
                return value;
            }
            return null;
        } catch (IllegalArgumentException e) {
            return null;
        }
    }

    private Object readValue() {
        if (index >= text.length()) {
            throw error("Unexpected end of input");
        }
        char c = text.charAt(index);
        switch (c) {
            case '{':
                return readObject();
            case '[':
                return readArray();
            case '"':
                return readString();
            case 't':
                expect("true");
                return Boolean.TRUE;
            case 'f':
                expect("false");
                return Boolean.FALSE;
            case 'n':
                expect("null");
                return null;
            default:
                if (c == '-' || (c >= '0' && c <= '9')) {
                    return readNumber();
                }
                throw error("Unexpected character '" + c + "'");
        }
    }

    private Map<String, Object> readObject() {
        Map<String, Object> object = new LinkedHashMap<>();
        index++;
        skipWhitespace();
        if (peek() == '}') {
            index++;
            return object;
        }
        while (true) {
            skipWhitespace();
            if (peek() != '"') {
                throw error("Expected a member name");
            }
            String name = readString();
            skipWhitespace();
            if (peek() != ':') {
                throw error("Expected ':'");
            }
            index++;
            skipWhitespace();
            object.put(name, readValue());
            skipWhitespace();
            char next = peek();
            index++;
            if (next == '}') {
                return object;
            }
            if (next != ',') {
                throw error("Expected ',' or '}'");
            }
        }
    }

    private List<Object> readArray() {
        List<Object> array = new ArrayList<>();
        index++;
        skipWhitespace();
        if (peek() == ']') {
            index++;
            return array;
        }
        while (true) {
            skipWhitespace();
            array.add(readValue());
            skipWhitespace();
            char next = peek();
            index++;
            if (next == ']') {
                return array;
            }
            if (next != ',') {
                throw error("Expected ',' or ']'");
            }
        }
    }

    private String readString() {
        index++;
        StringBuilder out = new StringBuilder();
        while (index < text.length()) {
            char c = text.charAt(index++);
            if (c == '"') {
                return out.toString();
            }
            if (c != '\\') {
                out.append(c);
                continue;
            }
            if (index >= text.length()) {
                break;
            }
            char escape = text.charAt(index++);
            switch (escape) {
                case '"', '\\', '/' -> out.append(escape);
                case 'n' -> out.append('\n');
                case 'r' -> out.append('\r');
                case 't' -> out.append('\t');
                case 'b' -> out.append('\b');
                case 'f' -> out.append('\f');
                case 'u' -> {
                    if (index + 4 > text.length()) {
                        throw error("Truncated \\u escape");
                    }
                    try {
                        out.append((char) Integer.parseInt(text.substring(index, index + 4), 16));
                    } catch (NumberFormatException e) {
                        throw error("Invalid \\u escape");
                    }
                    index += 4;
                }
                default -> throw error("Invalid escape '\\" + escape + "'");
            }
        }
        throw error("Unterminated string");
    }

    private Object readNumber() {
        int start = index;
        boolean whole = true;
        while (index < text.length()) {
            char c = text.charAt(index);
            if (c == '.' || c == 'e' || c == 'E') {
                whole = false;
            } else if (!(c == '-' || c == '+' || (c >= '0' && c <= '9'))) {
                break;
            }
            index++;
        }
        String number = text.substring(start, index);
        try {
            if (whole) {
                try {
                    return Long.valueOf(number);
                } catch (NumberFormatException e) {
                    // Too large for a long: fall through to a double.
                }
            }
            return Double.valueOf(number);
        } catch (NumberFormatException e) {
            throw error("Invalid number '" + number + "'");
        }
    }

    private void expect(String literal) {
        if (!text.startsWith(literal, index)) {
            throw error("Expected '" + literal + "'");
        }
        index += literal.length();
    }

    private char peek() {
        if (index >= text.length()) {
            throw error("Unexpected end of input");
        }
        return text.charAt(index);
    }

    private void skipWhitespace() {
        while (index < text.length()) {
            char c = text.charAt(index);
            if (c != ' ' && c != '\t' && c != '\n' && c != '\r') {
                return;
            }
            index++;
        }
    }

    private IllegalArgumentException error(String message) {
        return new IllegalArgumentException(message + " at offset " + index + " of the JSON response.");
    }
}
