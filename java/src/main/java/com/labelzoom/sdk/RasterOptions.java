package com.labelzoom.sdk;

/**
 * How an image or PDF source is rasterized before it becomes labels. Configured inside
 * {@code withRaster(r -> ...)}.
 *
 * <p>These act on the source picture, so they sit with {@code withSplit} and the label size, which
 * cut and frame the same picture. Rotation turns the finished label for every format, so it is a
 * target option.
 */
public final class RasterOptions {

    private final ConversionDraft draft;

    RasterOptions(ConversionDraft draft) {
        this.draft = draft;
    }

    /** Scales the picture, as a percentage. The server default is 100. */
    public RasterOptions scaling(float percent) {
        ParameterWriters.scaling(draft, percent);
        return this;
    }

    /**
     * Where the picture sits on its label, in output dots from the label's top-left corner. A
     * negative offset crops that much off its left or top, so with a label size this is how a
     * picture is cropped. Printer-language targets only.
     */
    public RasterOptions position(int x, int y) {
        ParameterWriters.position(draft, x, y);
        return this;
    }

    /**
     * Luminance threshold from 0 to 100 for reducing the picture to black and white. The server
     * default is 70. It also decides which labels {@link SplitOptions#skipBlank(boolean)} drops.
     */
    public RasterOptions darkness(int darkness) {
        ParameterWriters.darkness(draft, darkness);
        return this;
    }

    /** How the picture's color is reduced. The server default is {@link ColorMode#GRAYSCALE}. */
    public RasterOptions colorMode(ColorMode mode) {
        ParameterWriters.colorMode(draft, mode);
        return this;
    }
}
