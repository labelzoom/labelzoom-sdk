package com.labelzoom.sdk;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;

/**
 * Stand-ins for JDK 9-11 methods that Android lacks below API 33.
 *
 * <p>The SDK compiles with {@code release = 17}, so javac accepts {@code String.isBlank()} and
 * {@code InputStream.readAllBytes()}, but an Android app at a lower {@code minSdk} throws
 * {@code NoSuchMethodError} on them unless it enables core library desugaring. The build's
 * Animal Sniffer check against the Android API 26 signature keeps them out.
 */
final class Compat {

    private Compat() {
    }

    /** {@code String.isBlank()}: empty, or only whitespace code points. */
    static boolean isBlank(String value) {
        for (int i = 0; i < value.length(); ) {
            int codePoint = value.codePointAt(i);
            if (!Character.isWhitespace(codePoint)) {
                return false;
            }
            i += Character.charCount(codePoint);
        }
        return true;
    }

    /** {@code InputStream.readAllBytes()}. Does not close the stream. */
    static byte[] readAllBytes(InputStream in) throws IOException {
        ByteArrayOutputStream out = new ByteArrayOutputStream();
        byte[] buffer = new byte[8192];
        int read;
        while ((read = in.read(buffer)) != -1) {
            out.write(buffer, 0, read);
        }
        return out.toByteArray();
    }
}
