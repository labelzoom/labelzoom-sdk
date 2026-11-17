package com.labelzoom.sdk;

/**
 * Puts labels side by side in each printed row, for multi-column roll stock. Configured inside
 * {@code withLayout(l -> ...)}.
 *
 * <p>Applies to image or PDF to ZPL in {@link PdfConversionMode#IMAGE} mode. The response's
 * {@code ^PW} is the width of the whole row, so the label size stays the size of <b>one</b> label.
 */
public final class LayoutOptions {

    private final ConversionDraft draft;

    LayoutOptions(ConversionDraft draft) {
        this.draft = draft;
    }

    /** Labels across the roll, 1 to 10. The server default is 1. */
    public LayoutOptions across(int across) {
        return set("across", across);
    }

    /** Space between two labels in a row, <b>in inches</b>. The server default is 0. */
    public LayoutOptions gap(float gapInches) {
        return set("gap", gapInches);
    }

    /**
     * How labels fill the columns of a row. The server default is {@link LayoutFill#SEQUENTIAL};
     * use {@link LayoutFill#REPEAT} for a single image or a one-page file.
     */
    public LayoutOptions fill(LayoutFill fill) {
        return set("fill", fill.wireToken());
    }

    // Merged into the one "layout" object, never replacing it: contract section 4.1.
    private LayoutOptions set(String key, Object value) {
        draft.nested("layout").put(key, value);
        return this;
    }
}
