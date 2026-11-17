package com.labelzoom.sdk;

/**
 * Cuts each page of an image or PDF source into a grid of labels, read left to right and then down.
 * Configured inside {@code withSplit(s -> ...)}.
 *
 * <p>Applies to image or PDF to ZPL in {@link PdfConversionMode#IMAGE} mode. Every length is in
 * <b>inches</b> on the page as a viewer displays it, measured from its top-left corner. From the cut
 * onward, every other option sees labels rather than pages.
 */
public final class SplitOptions {

    private final ConversionDraft draft;

    SplitOptions(ConversionDraft draft) {
        this.draft = draft;
    }

    /** Rows of labels on each page. The server default is 1. */
    public SplitOptions rows(int rows) {
        return set("rows", rows);
    }

    /** Columns of labels on each page. The server default is 1. */
    public SplitOptions columns(int columns) {
        return set("columns", columns);
    }

    /** The size of one label on the page, <b>in inches</b>. Omit this to divide the page equally. */
    public SplitOptions labelSize(float widthInches, float heightInches) {
        return set("width", widthInches).set("height", heightInches);
    }

    /** Distance from the page's left and top edges to the first label, <b>in inches</b>. */
    public SplitOptions margins(float leftInches, float topInches) {
        return set("left", leftInches).set("top", topInches);
    }

    /** Space between two columns and between two rows of labels, <b>in inches</b>. */
    public SplitOptions gaps(float columnGapInches, float rowGapInches) {
        return set("columnGap", columnGapInches).set("rowGap", rowGapInches);
    }

    /** Leaves out labels with nothing printable, such as the unused end of a partly filled sheet. */
    public SplitOptions skipBlank(boolean skipBlank) {
        return set("skipBlank", skipBlank);
    }

    // Merged into the one "split" object, never replacing it: contract section 4.1.
    private SplitOptions set(String key, Object value) {
        draft.nested("split").put(key, value);
        return this;
    }
}
