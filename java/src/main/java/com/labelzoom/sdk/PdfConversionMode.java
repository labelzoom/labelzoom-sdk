package com.labelzoom.sdk;

/** How a source PDF is interpreted. */
public enum PdfConversionMode {

    /** Rasterize the page and trace it. The server default. */
    IMAGE,

    /** Read the PDF's native drawing operations. */
    NATIVE;

    /** The exact uppercase token the API expects. */
    public String wireToken() {
        return name();
    }
}
