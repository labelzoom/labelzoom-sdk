package com.labelzoom.sdk;

/** Colour handling when rasterizing or tracing images. */
public enum ColorMode {

    /** Two-colour black and white. */
    BW,

    /** Greyscale. The server default. */
    GRAYSCALE,

    /** Full colour. */
    COLOR;

    /** The exact uppercase token the API expects. */
    public String wireToken() {
        return name();
    }
}
