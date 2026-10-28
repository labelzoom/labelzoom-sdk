package com.labelzoom.sdk;

/**
 * How pages fill the columns of a multi-column row, when
 * {@link ConversionTargetBuilder#withLayout(int, float)} puts more than one label across.
 */
public enum LayoutFill {

    /** Each page goes in the next column. The server default. */
    SEQUENTIAL,

    /** Each page fills the whole row. */
    REPEAT;

    /** The exact uppercase token the API expects. */
    public String wireToken() {
        return name();
    }
}
