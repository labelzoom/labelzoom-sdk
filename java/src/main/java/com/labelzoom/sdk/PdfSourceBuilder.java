package com.labelzoom.sdk;

import java.util.function.Consumer;

/** The source stage for a PDF document. A PDF has no dots, so there is no {@code withDpi}. */
public final class PdfSourceBuilder extends SourceStage<PdfSourceBuilder> {

    PdfSourceBuilder(ConversionDraft draft) {
        super(draft);
    }

    /** Converts one page, by <b>0-based</b> index. Omit this to convert every page. */
    public PdfSourceBuilder withPage(int zeroBasedPageNumber) {
        ParameterWriters.pdfPage(draft, zeroBasedPageNumber);
        return this;
    }

    /** How the PDF is read. The server default is {@link PdfConversionMode#IMAGE}, which splitting and layout require. */
    public PdfSourceBuilder withConversionMode(PdfConversionMode mode) {
        ParameterWriters.pdfConversionMode(draft, mode);
        return this;
    }

    /**
     * Cuts each page into a grid of labels, read left to right and then down. ZPL target only.
     *
     * <p>Example: {@code .withSplit(s -> s.rows(2).columns(2).skipBlank(true))}
     */
    public PdfSourceBuilder withSplit(Consumer<SplitOptions> configure) {
        ParameterWriters.split(draft, configure);
        return this;
    }

    /**
     * How the picture is rasterized: scaling, its position on the label (which, with a label size,
     * crops it), the black-and-white threshold and the color reduction.
     *
     * <p>Example: {@code .withRaster(r -> r.scaling(150).position(-20, 0).darkness(60))}
     */
    public PdfSourceBuilder withRaster(Consumer<RasterOptions> configure) {
        ParameterWriters.raster(draft, configure);
        return this;
    }
}
