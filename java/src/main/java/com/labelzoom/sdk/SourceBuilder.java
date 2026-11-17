package com.labelzoom.sdk;

/** The source stage for LabelZoom XML and JSON. They record their own resolution, so there is no {@code withDpi}. */
public final class SourceBuilder extends SourceStage<SourceBuilder> {

    SourceBuilder(ConversionDraft draft) {
        super(draft);
    }
}
