package com.labelzoom.sdk;

/** The target stage for PDF. A PDF has no resolution of its own, so there is no {@code withDpi}. */
public final class PdfTargetBuilder extends TargetStage<PdfTargetBuilder> {

    PdfTargetBuilder(ConversionDraft draft) {
        super(draft, TargetFormat.PDF);
    }
}
