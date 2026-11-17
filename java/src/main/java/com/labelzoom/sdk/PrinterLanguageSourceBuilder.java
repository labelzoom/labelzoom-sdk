package com.labelzoom.sdk;

/** The source stage for EPL, IPL, TSPL, DPL and SBPL. */
public final class PrinterLanguageSourceBuilder extends SourceStage<PrinterLanguageSourceBuilder> {

    PrinterLanguageSourceBuilder(ConversionDraft draft) {
        super(draft);
    }

    /**
     * The resolution the source was made for, in dots per inch: how its dots or pixels map to
     * inches. The server default is 203. The output follows it unless the target stage sets its own.
     */
    public PrinterLanguageSourceBuilder withDpi(int dpi) {
        ParameterWriters.sourceDpi(draft, dpi);
        return this;
    }
}
