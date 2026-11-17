package com.labelzoom.sdk;

/**
 * Prints EPL, IPL, TSPL, DPL, SBPL, Fingerprint or CPCL. The print twin of
 * {@link PrinterLanguageSourceBuilder}.
 */
public final class PrinterLanguagePrintBuilder extends PrintStage<PrinterLanguagePrintBuilder> {

    PrinterLanguagePrintBuilder(PrintDraft draft) {
        super(draft);
    }

    /**
     * The resolution the document was made for, in dots per inch: how its dots map to inches. The
     * server default is 203. Set it when the document was written for a 300 or 600 dpi head.
     */
    public PrinterLanguagePrintBuilder withDpi(int dpi) {
        ParameterWriters.sourceDpi(document(), dpi);
        return this;
    }
}
