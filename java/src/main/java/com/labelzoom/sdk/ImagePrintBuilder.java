package com.labelzoom.sdk;

import java.util.function.Consumer;

/** Prints a PNG, BMP, GIF or JPEG image. The print twin of {@link ImageSourceBuilder}. */
public final class ImagePrintBuilder extends PrintStage<ImagePrintBuilder> {

    ImagePrintBuilder(PrintDraft draft) {
        super(draft);
    }

    /**
     * The image's resolution, in dots per inch: how its pixels map to inches. The server default is
     * 203.
     */
    public ImagePrintBuilder withDpi(int dpi) {
        ParameterWriters.sourceDpi(document(), dpi);
        return this;
    }

    /**
     * Cuts each image into a grid of labels, read left to right and then down.
     *
     * <p>Example: {@code .withSplit(s -> s.rows(2).columns(2).skipBlank(true))}
     */
    public ImagePrintBuilder withSplit(Consumer<SplitOptions> configure) {
        ParameterWriters.split(document(), configure);
        return this;
    }

    /**
     * How the image is rasterized: scaling, its position on the label (which, with a label size,
     * crops it), the black-and-white threshold and the color reduction.
     *
     * <p>Example: {@code .withRaster(r -> r.scaling(150).position(-20, 0).darkness(60))}
     */
    public ImagePrintBuilder withRaster(Consumer<RasterOptions> configure) {
        ParameterWriters.raster(document(), configure);
        return this;
    }
}
