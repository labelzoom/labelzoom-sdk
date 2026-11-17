package com.labelzoom.sdk;

import java.util.function.Consumer;

/** The source stage for a PNG, BMP, GIF or JPEG image. */
public final class ImageSourceBuilder extends SourceStage<ImageSourceBuilder> {

    ImageSourceBuilder(ConversionDraft draft) {
        super(draft);
    }

    /**
     * The resolution the source was made for, in dots per inch: how its dots or pixels map to
     * inches. The server default is 203. The output follows it unless the target stage sets its own.
     */
    public ImageSourceBuilder withDpi(int dpi) {
        ParameterWriters.sourceDpi(draft, dpi);
        return this;
    }

    /**
     * Cuts each page into a grid of labels, read left to right and then down. ZPL target only.
     *
     * <p>Example: {@code .withSplit(s -> s.rows(2).columns(2).skipBlank(true))}
     */
    public ImageSourceBuilder withSplit(Consumer<SplitOptions> configure) {
        ParameterWriters.split(draft, configure);
        return this;
    }

    /**
     * How the picture is rasterized: scaling, its position on the label (which, with a label size,
     * crops it), the black-and-white threshold and the color reduction.
     *
     * <p>Example: {@code .withRaster(r -> r.scaling(150).position(-20, 0).darkness(60))}
     */
    public ImageSourceBuilder withRaster(Consumer<RasterOptions> configure) {
        ParameterWriters.raster(draft, configure);
        return this;
    }
}
