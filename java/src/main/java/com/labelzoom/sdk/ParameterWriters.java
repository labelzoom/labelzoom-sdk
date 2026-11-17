package com.labelzoom.sdk;

import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.function.Consumer;

/**
 * The validation and wire mapping for every option, written once.
 *
 * <p>Several builders expose the same option: {@link PdfSourceBuilder#withPage(int)} and
 * {@link DynamicSourceBuilder#withPdfPage(int)} both write {@code pdf.pageNumber}. Each public
 * method is a one-line call into here, so the builders cannot drift apart on validation or on the
 * JSON path.
 */
final class ParameterWriters {

    private ParameterWriters() {
    }

    // Contract C8: sourceDpi from the source stage, targetDpi from the target stage, never dpi.
    static void sourceDpi(ConversionDraft d, int dpi) {
        d.params.put("sourceDpi", positiveDpi(dpi));
    }

    static void targetDpi(ConversionDraft d, int dpi) {
        d.params.put("targetDpi", positiveDpi(dpi));
    }

    private static int positiveDpi(int dpi) {
        if (dpi <= 0) {
            throw new LabelZoomValidationException("dpi", "DPI must be greater than zero.");
        }
        return dpi;
    }

    static void labelSize(ConversionDraft d, float widthInches, float heightInches) {
        if (widthInches <= 0 || heightInches <= 0) {
            throw new LabelZoomValidationException(
                    "label", "Label width and height must be greater than zero.");
        }
        Map<String, Object> label = d.nested("label");
        label.put("width", widthInches);
        label.put("height", heightInches);
    }

    // No local range check: contract section 5 leaves labelIndex limits to the server.
    static void labelIndex(ConversionDraft d, int zeroBasedIndex) {
        d.params.put("labelIndex", zeroBasedIndex);
    }

    static void data(ConversionDraft d, List<? extends Map<String, ?>> records) {
        if (records == null || records.isEmpty()) {
            throw new LabelZoomValidationException(
                    "data", "Provide at least one data record, or omit this call entirely.");
        }
        List<Object> normalized = new ArrayList<>(records.size());
        for (int i = 0; i < records.size(); i++) {
            Map<String, ?> record = records.get(i);
            if (record == null) {
                throw new LabelZoomValidationException(
                        "data", "data[" + i + "] is null; every entry must be an object.");
            }
            normalized.add(record);
        }
        d.params.put("data", normalized);
    }

    static void pdfPage(ConversionDraft d, int zeroBasedPageNumber) {
        if (zeroBasedPageNumber < 0) {
            throw new LabelZoomValidationException(
                    "pdf", "Page number is 0-based and cannot be negative; 0 selects the first page.");
        }
        d.nested("pdf").put("pageNumber", zeroBasedPageNumber);
    }

    static void pdfConversionMode(ConversionDraft d, PdfConversionMode mode) {
        d.nested("pdf").put("conversionMode", mode.wireToken());
    }

    static void zplCommandsToIgnore(ConversionDraft d, String... commands) {
        if (commands == null || commands.length == 0) {
            throw new LabelZoomValidationException(
                    "zpl", "Provide at least one command, or omit this call entirely.");
        }
        d.nested("zpl").put("commandsToIgnore", List.of(commands));
    }

    static void split(ConversionDraft d, Consumer<SplitOptions> configure) {
        if (configure == null) {
            throw new LabelZoomValidationException("split", "Split configuration cannot be null.");
        }
        configure.accept(new SplitOptions(d));
    }

    static void raster(ConversionDraft d, Consumer<RasterOptions> configure) {
        if (configure == null) {
            throw new LabelZoomValidationException("raster", "Raster configuration cannot be null.");
        }
        configure.accept(new RasterOptions(d));
    }

    static void scaling(ConversionDraft d, float percent) {
        if (percent <= 0) {
            throw new LabelZoomValidationException("scaling", "Scaling must be greater than zero.");
        }
        d.params.put("scaling", percent);
    }

    static void position(ConversionDraft d, int x, int y) {
        Map<String, Object> position = d.nested("position");
        position.put("x", x);
        position.put("y", y);
    }

    static void darkness(ConversionDraft d, int darkness) {
        if (darkness < 0 || darkness > 100) {
            throw new LabelZoomValidationException(
                    "darkness", "Darkness must be between 0 and 100, but was " + darkness + ".");
        }
        d.params.put("darkness", darkness);
    }

    static void colorMode(ConversionDraft d, ColorMode mode) {
        d.params.put("colorMode", mode.wireToken());
    }

    static void rotation(ConversionDraft d, int rotation) {
        // Rejected locally: the server would 400, and this is unambiguously a caller bug.
        if (rotation % 90 != 0) {
            throw new LabelZoomValidationException(
                    "rotation", "Rotation must be a multiple of 90 degrees, but was " + rotation + ".");
        }
        d.params.put("rotation", rotation);
    }

    static void dialect(ConversionDraft d, String dialect) {
        if (dialect == null || Compat.isBlank(dialect)) {
            throw new LabelZoomValidationException("dialect", "Dialect cannot be null or blank.");
        }
        d.params.put("dialect", dialect);
    }

    static void zplImageCompression(ConversionDraft d, ZplImageCompression compression) {
        d.nested("zpl").put("imageCompression", compression.wireToken());
    }

    static void zplCollapseCopies(ConversionDraft d, boolean collapse) {
        d.nested("zpl").put("collapseCopies", collapse);
    }

    static void layout(ConversionDraft d, Consumer<LayoutOptions> configure) {
        if (configure == null) {
            throw new LabelZoomValidationException("layout", "Layout configuration cannot be null.");
        }
        configure.accept(new LayoutOptions(d));
    }

    static void parameter(ConversionDraft d, String key, Object value) {
        if (key == null || Compat.isBlank(key)) {
            throw new LabelZoomValidationException("params", "Parameter key cannot be null or blank.");
        }
        d.params.put(key, value);
    }

    static void rawQueryParameter(ConversionDraft d, String key, String value) {
        if (key == null || Compat.isBlank(key)) {
            throw new LabelZoomValidationException(
                    "params", "Query parameter key cannot be null or blank.");
        }
        d.rawQuery.put(key, value == null ? "" : value);
    }
}
