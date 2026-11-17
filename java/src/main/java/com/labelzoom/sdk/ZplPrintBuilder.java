package com.labelzoom.sdk;

/** Prints a ZPL document. The print twin of {@link ZplSourceBuilder}. */
public final class ZplPrintBuilder extends PrintStage<ZplPrintBuilder> {

    ZplPrintBuilder(PrintDraft draft) {
        super(draft);
    }

    /**
     * The resolution the document was made for, in dots per inch: how its dots map to inches. The
     * server default is 203. Set it when the document was written for a 300 or 600 dpi head.
     */
    public ZplPrintBuilder withDpi(int dpi) {
        ParameterWriters.sourceDpi(document(), dpi);
        return this;
    }

    /** ZPL commands to skip while reading the document, for example {@code ^PQ}. */
    public ZplPrintBuilder withCommandsToIgnore(String... commands) {
        ParameterWriters.zplCommandsToIgnore(document(), commands);
        return this;
    }
}
