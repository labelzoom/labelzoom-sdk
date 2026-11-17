package com.labelzoom.sdk;

/**
 * The source stage for LabelZoom XML and JSON, NiceLabel, BarTender and DYMO. These record their own
 * resolution or measure in physical units, so there is no {@code withDpi}.
 */
public final class SourceBuilder extends SourceStage<SourceBuilder> {

    SourceBuilder(ConversionDraft draft) {
        super(draft);
    }
}
