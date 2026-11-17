package com.labelzoom.sdk;

/** The source stage for a ZPL or ESC/Label document. */
public final class ZplSourceBuilder extends SourceStage<ZplSourceBuilder> {

    ZplSourceBuilder(ConversionDraft draft) {
        super(draft);
    }

    /**
     * The resolution the source was made for, in dots per inch: how its dots or pixels map to
     * inches. The server default is 203. The output follows it unless the target stage sets its own.
     */
    public ZplSourceBuilder withDpi(int dpi) {
        ParameterWriters.sourceDpi(draft, dpi);
        return this;
    }

    /** ZPL commands the parser should skip, for example {@code ^PQ}. */
    public ZplSourceBuilder withCommandsToIgnore(String... commands) {
        ParameterWriters.zplCommandsToIgnore(draft, commands);
        return this;
    }
}
