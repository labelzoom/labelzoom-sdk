package com.labelzoom.sdk;

/** The target stage for a PNG, BMP, GIF or JPEG image. */
public final class ImageTargetBuilder extends TargetStage<ImageTargetBuilder> {

    ImageTargetBuilder(ConversionDraft draft, TargetFormat target) {
        super(draft, target);
    }

    /**
     * The resolution of the output, in dots per inch. The server default is the source's
     * resolution, else 203. To move a label to a printer of another density at the same physical
     * size, set the source's resolution too.
     */
    public ImageTargetBuilder withDpi(int dpi) {
        ParameterWriters.targetDpi(draft(), dpi);
        return this;
    }

    /** {@link ColorMode#BW} renders a black-and-white image at the {@code withDarkness} threshold; any other mode renders in color, the default. */
    public ImageTargetBuilder withColorMode(ColorMode mode) {
        ParameterWriters.colorMode(draft(), mode);
        return this;
    }

    /** Luminance threshold from 0 to 100 for a black-and-white image. The server default is 70. */
    public ImageTargetBuilder withDarkness(int darkness) {
        ParameterWriters.darkness(draft(), darkness);
        return this;
    }
}
