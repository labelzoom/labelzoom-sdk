package com.labelzoom.sdk;

/**
 * Prints LabelZoom XML or JSON. These record their own resolution, so there is no {@code withDpi}.
 * The print twin of {@link SourceBuilder}.
 */
public final class PrintBuilder extends PrintStage<PrintBuilder> {

    PrintBuilder(PrintDraft draft) {
        super(draft);
    }
}
