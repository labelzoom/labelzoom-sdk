package com.labelzoom.sdk;

/** The target stage for EPL, IPL, TSPL, DPL, SBPL, LabelZoom XML and LabelZoom JSON. */
public final class TargetBuilder extends TargetStage<TargetBuilder> {

    TargetBuilder(ConversionDraft draft, TargetFormat target) {
        super(draft, target);
    }

    /**
     * The resolution of the output, in dots per inch. The server default is the source's
     * resolution, else 203. To move a label to a printer of another density at the same physical
     * size, set the source's resolution too.
     */
    public TargetBuilder withDpi(int dpi) {
        ParameterWriters.targetDpi(draft(), dpi);
        return this;
    }
}
