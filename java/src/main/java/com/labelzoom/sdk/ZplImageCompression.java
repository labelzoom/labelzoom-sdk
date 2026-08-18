package com.labelzoom.sdk;

/** Image compression used when writing ZPL. */
public enum ZplImageCompression {

    /** Base-64 encoded, DEFLATE compressed. The server default. */
    Z64,

    /** Run-length encoded hexadecimal. */
    COMPRESSED_HEX;

    /** The exact uppercase token the API expects. */
    public String wireToken() {
        return name();
    }
}
