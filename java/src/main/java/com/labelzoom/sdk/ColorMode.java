package com.labelzoom.sdk;

/** Color handling when rasterizing or tracing images. */
public enum ColorMode {

    /** Two-color black and white. */
    BW,

    /** Grayscale. The server default. */
    GRAYSCALE,

    /** Full color. */
    COLOR;

    /** The exact uppercase token the API expects. */
    public String wireToken() {
        return name();
    }
}
