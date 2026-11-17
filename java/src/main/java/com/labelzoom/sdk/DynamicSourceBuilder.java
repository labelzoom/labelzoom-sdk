package com.labelzoom.sdk;

import java.util.function.Consumer;

/** The source stage for a format chosen at run time. The compiler can't know the format here, so every source option is available, named with the format it applies to; the server ignores one that doesn't apply. */
public final class DynamicSourceBuilder extends SourceStage<DynamicSourceBuilder> {

    DynamicSourceBuilder(ConversionDraft draft) {
        super(draft);
    }

    /**
     * The resolution the source was made for, in dots per inch: how its dots or pixels map to
     * inches. The server default is 203. The output follows it unless the target stage sets its own.
     */
    public DynamicSourceBuilder withDpi(int dpi) {
        ParameterWriters.sourceDpi(draft, dpi);
        return this;
    }

    /** PDF source: converts one page, by <b>0-based</b> index. */
    public DynamicSourceBuilder withPdfPage(int zeroBasedPageNumber) {
        ParameterWriters.pdfPage(draft, zeroBasedPageNumber);
        return this;
    }

    /** PDF source: how it is read. The server default is {@link PdfConversionMode#IMAGE}. */
    public DynamicSourceBuilder withPdfConversionMode(PdfConversionMode mode) {
        ParameterWriters.pdfConversionMode(draft, mode);
        return this;
    }

    /** ZPL source: commands the parser should skip, for example {@code ^PQ}. */
    public DynamicSourceBuilder withZplCommandsToIgnore(String... commands) {
        ParameterWriters.zplCommandsToIgnore(draft, commands);
        return this;
    }

    /**
     * Cuts each page into a grid of labels, read left to right and then down. ZPL target only.
     *
     * <p>Example: {@code .withSplit(s -> s.rows(2).columns(2).skipBlank(true))}
     */
    public DynamicSourceBuilder withSplit(Consumer<SplitOptions> configure) {
        ParameterWriters.split(draft, configure);
        return this;
    }

    /**
     * How the picture is rasterized: scaling, its position on the label (which, with a label size,
     * crops it), the black-and-white threshold and the color reduction.
     *
     * <p>Example: {@code .withRaster(r -> r.scaling(150).position(-20, 0).darkness(60))}
     */
    public DynamicSourceBuilder withRaster(Consumer<RasterOptions> configure) {
        ParameterWriters.raster(draft, configure);
        return this;
    }
}
