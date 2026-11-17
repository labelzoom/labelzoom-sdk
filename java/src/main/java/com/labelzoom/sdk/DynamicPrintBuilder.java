package com.labelzoom.sdk;

import java.util.function.Consumer;

/**
 * Prints a document whose format is chosen at run time. The compiler can't know the format here, so
 * every source option is available, named with the format it applies to; the server ignores one
 * that doesn't apply. The print twin of {@link DynamicSourceBuilder}.
 */
public final class DynamicPrintBuilder extends PrintStage<DynamicPrintBuilder> {

    DynamicPrintBuilder(PrintDraft draft) {
        super(draft);
    }

    /**
     * Printer-language or image document: the resolution it was made for, in dots per inch. The
     * server default is 203.
     */
    public DynamicPrintBuilder withDpi(int dpi) {
        ParameterWriters.sourceDpi(document(), dpi);
        return this;
    }

    /** PDF document: prints one page, by <b>0-based</b> index. */
    public DynamicPrintBuilder withPdfPage(int zeroBasedPageNumber) {
        ParameterWriters.pdfPage(document(), zeroBasedPageNumber);
        return this;
    }

    /** PDF document: how it is read. The server default is {@link PdfConversionMode#IMAGE}. */
    public DynamicPrintBuilder withPdfConversionMode(PdfConversionMode mode) {
        ParameterWriters.pdfConversionMode(document(), mode);
        return this;
    }

    /** ZPL document: commands to skip while reading it, for example {@code ^PQ}. */
    public DynamicPrintBuilder withZplCommandsToIgnore(String... commands) {
        ParameterWriters.zplCommandsToIgnore(document(), commands);
        return this;
    }

    /**
     * PDF or image document: cuts each page into a grid of labels, read left to right and then down.
     *
     * <p>Example: {@code .withSplit(s -> s.rows(2).columns(2).skipBlank(true))}
     */
    public DynamicPrintBuilder withSplit(Consumer<SplitOptions> configure) {
        ParameterWriters.split(document(), configure);
        return this;
    }

    /**
     * PDF or image document: scaling, its position on the label, the black-and-white threshold and
     * the color reduction.
     *
     * <p>Example: {@code .withRaster(r -> r.scaling(150).position(-20, 0).darkness(60))}
     */
    public DynamicPrintBuilder withRaster(Consumer<RasterOptions> configure) {
        ParameterWriters.raster(document(), configure);
        return this;
    }
}
