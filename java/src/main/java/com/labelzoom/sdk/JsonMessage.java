package com.labelzoom.sdk;

/**
 * Extracts a top-level {@code "message"} string from an error body.
 *
 * <p>Deliberately not a JSON parser. The SDK ships with zero runtime dependencies, and the only
 * thing it ever needs to read from a response is this one field. Anything it cannot understand
 * falls back to the raw body, which is the correct behaviour anyway.
 */
final class JsonMessage {

    private JsonMessage() {
    }

    static String extract(String json) {
        int index = indexOfKey(json, "\"message\"");
        if (index < 0) {
            return null;
        }
        index = json.indexOf(':', index);
        if (index < 0) {
            return null;
        }
        index++;
        while (index < json.length() && Character.isWhitespace(json.charAt(index))) {
            index++;
        }
        if (index >= json.length() || json.charAt(index) != '"') {
            return null;
        }
        index++;

        StringBuilder out = new StringBuilder();
        while (index < json.length()) {
            char c = json.charAt(index++);
            if (c == '"') {
                return out.toString();
            }
            if (c != '\\') {
                out.append(c);
                continue;
            }
            if (index >= json.length()) {
                return null;
            }
            char escape = json.charAt(index++);
            switch (escape) {
                case 'n' -> out.append('\n');
                case 'r' -> out.append('\r');
                case 't' -> out.append('\t');
                case 'b' -> out.append('\b');
                case 'f' -> out.append('\f');
                case 'u' -> {
                    if (index + 4 > json.length()) {
                        return null;
                    }
                    out.append((char) Integer.parseInt(json.substring(index, index + 4), 16));
                    index += 4;
                }
                default -> out.append(escape);
            }
        }
        // Unterminated string: the body is malformed, so let the caller fall back to raw text.
        return null;
    }

    /** Finds a key only outside string literals, so a value containing it is not mistaken for one. */
    private static int indexOfKey(String json, String key) {
        boolean inString = false;
        for (int i = 0; i < json.length(); i++) {
            char c = json.charAt(i);
            if (inString) {
                if (c == '\\') {
                    i++;
                } else if (c == '"') {
                    inString = false;
                }
                continue;
            }
            if (c == '"') {
                if (json.startsWith(key, i)) {
                    return i;
                }
                inString = true;
            }
        }
        return -1;
    }
}
