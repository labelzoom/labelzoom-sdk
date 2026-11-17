package com.labelzoom.sdk;

import java.util.function.Consumer;

/**
 * Prints a PDF document. A PDF has no dots, so there is no {@code withDpi}. The print twin of
 * {@link PdfSourceBuilder}.
 */
public final class PdfPrintBuilder extends PrintStage<PdfPrintBuilder> {

    PdfPrintBuilder(PrintDraft draft) {
        super(draft);
    }

    /** Prints one page, by <b>0-based</b> index. Omit this to print every page. */
    public PdfPrintBuilder withPage(int zeroBasedPageNumber) {
        ParameterWriters.pdfPage(document(), zeroBasedPageNumber);
        return this;
    }

    /** How the PDF is read. The server default is {@link PdfConversionMode#IMAGE}, which splitting requires. */
    public PdfPrintBuilder withConversionMode(PdfConversionMode mode) {
        ParameterWriters.pdfConversionMode(document(), mode);
        return this;
    }

    /**
     * Cuts each page into a grid of labels, read left to right and then down.
     *
     * <p>Example: {@code .withSplit(s -> s.rows(2).columns(2).skipBlank(true))}
     */
    public PdfPrintBuilder withSplit(Consumer<SplitOptions> configure) {
        ParameterWriters.split(document(), configure);
        return this;
    }

    /**
     * How each page is rasterized: scaling, its position on the label (which, with a label size,
     * crops it), the black-and-white threshold and the color reduction.
     *
     * <p>Example: {@code .withRaster(r -> r.scaling(150).position(-20, 0).darkness(60))}
     */
    public PdfPrintBuilder withRaster(Consumer<RasterOptions> configure) {
        ParameterWriters.raster(document(), configure);
        return this;
    }
}
