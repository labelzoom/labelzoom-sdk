package com.labelzoom.sdk;

import java.util.function.Consumer;

/** The target stage for a format chosen at run time. The compiler can't know the format here, so every target option is available, named with the format it applies to. {@code withColorMode} and {@code withDarkness} write the same keys as the source stage's {@code withRaster}: the last value set is sent. */
public final class DynamicTargetBuilder extends TargetStage<DynamicTargetBuilder> {

    DynamicTargetBuilder(ConversionDraft draft, TargetFormat target) {
        super(draft, target);
    }

    /**
     * The resolution of the output, in dots per inch. The server default is the source's
     * resolution, else 203. To move a label to a printer of another density at the same physical
     * size, set the source's resolution too.
     */
    public DynamicTargetBuilder withDpi(int dpi) {
        ParameterWriters.targetDpi(draft(), dpi);
        return this;
    }

    /** ZPL target: image compression. The server default is {@link ZplImageCompression#Z64}. */
    public DynamicTargetBuilder withZplImageCompression(ZplImageCompression compression) {
        ParameterWriters.zplImageCompression(draft(), compression);
        return this;
    }

    /** ZPL target, image or PDF source: each run of identical labels becomes one label with a {@code ^PQ} count. */
    public DynamicTargetBuilder withZplCollapseCopies(boolean collapse) {
        ParameterWriters.zplCollapseCopies(draft(), collapse);
        return this;
    }

    /**
     * Image or PDF source, IMAGE mode: labels side by side in each printed row.
     *
     * <p>Example: {@code .withLayout(l -> l.across(2).gap(0.125f))}
     */
    public DynamicTargetBuilder withLayout(Consumer<LayoutOptions> configure) {
        ParameterWriters.layout(draft(), configure);
        return this;
    }

    /** {@link ColorMode#BW} renders a black-and-white image at the {@code withDarkness} threshold; any other mode renders in color, the default. */
    public DynamicTargetBuilder withColorMode(ColorMode mode) {
        ParameterWriters.colorMode(draft(), mode);
        return this;
    }

    /** Luminance threshold from 0 to 100 for a black-and-white image. The server default is 70. */
    public DynamicTargetBuilder withDarkness(int darkness) {
        ParameterWriters.darkness(draft(), darkness);
        return this;
    }
}
