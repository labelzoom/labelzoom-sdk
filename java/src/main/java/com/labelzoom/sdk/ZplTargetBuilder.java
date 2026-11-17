package com.labelzoom.sdk;

import java.util.function.Consumer;

/** The target stage for ZPL output. */
public final class ZplTargetBuilder extends TargetStage<ZplTargetBuilder> {

    ZplTargetBuilder(ConversionDraft draft) {
        super(draft, TargetFormat.ZPL);
    }

    /**
     * The resolution of the output, in dots per inch. The server default is the source's
     * resolution, else 203. To move a label to a printer of another density at the same physical
     * size, set the source's resolution too.
     */
    public ZplTargetBuilder withDpi(int dpi) {
        ParameterWriters.targetDpi(draft(), dpi);
        return this;
    }

    /** Image compression in the ZPL output. The server default is {@link ZplImageCompression#Z64}. */
    public ZplTargetBuilder withImageCompression(ZplImageCompression compression) {
        ParameterWriters.zplImageCompression(draft(), compression);
        return this;
    }

    /** Image or PDF source: each run of identical labels becomes one label with a {@code ^PQ} count. The server default is {@code false}. */
    public ZplTargetBuilder withCollapseCopies(boolean collapse) {
        ParameterWriters.zplCollapseCopies(draft(), collapse);
        return this;
    }

    /**
     * Image or PDF source, IMAGE mode: labels side by side in each printed row.
     *
     * <p>Example: {@code .withLayout(l -> l.across(2).gap(0.125f))}
     */
    public ZplTargetBuilder withLayout(Consumer<LayoutOptions> configure) {
        ParameterWriters.layout(draft(), configure);
        return this;
    }
}
