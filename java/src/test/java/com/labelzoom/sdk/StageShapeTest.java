package com.labelzoom.sdk;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertInstanceOf;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import java.lang.reflect.Method;
import java.lang.reflect.Modifier;
import java.net.URI;
import java.net.URLDecoder;
import java.nio.charset.StandardCharsets;
import java.util.List;
import java.util.Map;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.CsvSource;
import org.junit.jupiter.params.provider.ValueSource;

/**
 * Pins which options each stage of the fluent chain offers.
 *
 * <p>The staged builders exist so that an option can only be set where it acts:
 * {@code fromZpl(zpl).withPage(0)} must not compile, and neither may {@code toPdf().withDpi(300)}. A
 * test can't assert a compile error about itself, so these assert what makes the compile error
 * certain: the method isn't a public member of that builder.
 *
 * <p>The conformance suite runs through {@link DynamicSourceBuilder} and {@link DynamicTargetBuilder},
 * because its fixtures pick formats at run time. The last tests here send requests through the typed
 * builders, so that path is checked on the wire too.
 */
class StageShapeTest {

    private static final ObjectMapper MAPPER = new ObjectMapper();

    private static final List<Class<?>> SOURCE_STAGES = List.of(
            SourceBuilder.class, PrinterLanguageSourceBuilder.class, ZplSourceBuilder.class,
            ImageSourceBuilder.class, PdfSourceBuilder.class, DynamicSourceBuilder.class);

    private static final List<Class<?>> TARGET_STAGES = List.of(
            TargetBuilder.class, PdfTargetBuilder.class, ZplTargetBuilder.class,
            ImageTargetBuilder.class, DynamicTargetBuilder.class);

    @ParameterizedTest
    @CsvSource({
        "ZplSourceBuilder, withPage",
        "ZplSourceBuilder, withSplit",
        "ZplSourceBuilder, withRaster",
        "PrinterLanguageSourceBuilder, withPage",
        "PrinterLanguageSourceBuilder, withRaster",
        "SourceBuilder, withDpi",
        "SourceBuilder, withCommandsToIgnore",
        "ImageSourceBuilder, withPage",
        "PdfSourceBuilder, withDpi",
        "PdfSourceBuilder, withCommandsToIgnore",
        "PdfTargetBuilder, withDpi",
        "TargetBuilder, withLayout",
        "TargetBuilder, withCollapseCopies",
        "TargetBuilder, withColorMode",
        "ZplTargetBuilder, withColorMode",
        "ImageTargetBuilder, withLayout",
    })
    void formatSpecificOptionIsAbsentFromOtherFormats(String builder, String method) throws Exception {
        assertFalse(hasMethod(type(builder), method), builder + " must not offer " + method);
    }

    @ParameterizedTest
    @CsvSource({
        "ZplSourceBuilder, withDpi",
        "ZplSourceBuilder, withCommandsToIgnore",
        "PrinterLanguageSourceBuilder, withDpi",
        "ImageSourceBuilder, withDpi",
        "ImageSourceBuilder, withSplit",
        "ImageSourceBuilder, withRaster",
        "PdfSourceBuilder, withPage",
        "PdfSourceBuilder, withConversionMode",
        "PdfSourceBuilder, withSplit",
        "PdfSourceBuilder, withRaster",
        "DynamicSourceBuilder, withDpi",
        "DynamicSourceBuilder, withRaster",
        "TargetBuilder, withDpi",
        "ZplTargetBuilder, withDpi",
        "ZplTargetBuilder, withLayout",
        "ZplTargetBuilder, withCollapseCopies",
        "ZplTargetBuilder, withImageCompression",
        "ImageTargetBuilder, withDpi",
        "ImageTargetBuilder, withColorMode",
        "ImageTargetBuilder, withDarkness",
        "DynamicTargetBuilder, withDpi",
        "DynamicTargetBuilder, withColorMode",
    })
    void formatSpecificOptionIsOnItsFormat(String builder, String method) throws Exception {
        assertTrue(hasMethod(type(builder), method), builder + " must offer " + method);
    }

    @ParameterizedTest
    @ValueSource(strings = {"withRotation", "withDialect", "withWatermark", "withLayout", "execute"})
    void sourceStagesOfferNoTargetOptions(String method) {
        for (Class<?> stage : SOURCE_STAGES) {
            assertFalse(hasMethod(stage, method), stage.getSimpleName() + " must not offer " + method);
        }
    }

    @ParameterizedTest
    @ValueSource(strings = {
        "withLabelSize", "withLabelIndex", "withData", "withPage", "withPdfPage", "withSplit",
        "withRaster", "withScaling", "withPosition", "withZplCommandsToIgnore"})
    void targetStagesOfferNoSourceOptions(String method) {
        for (Class<?> stage : TARGET_STAGES) {
            assertFalse(hasMethod(stage, method), stage.getSimpleName() + " must not offer " + method);
        }
    }

    @Test
    void namedMethodsReturnTheStageForTheirFormat() {
        try (LabelZoomClient client = LabelZoomClient.builder().apiKey(null).build()) {
            assertInstanceOf(ZplSourceBuilder.class, client.convert().fromZpl("^XA^XZ"));
            assertInstanceOf(PdfSourceBuilder.class, client.convert().fromPdf(new byte[] {1}));
            assertInstanceOf(ImageSourceBuilder.class, client.convert().fromPng(new byte[] {1}));
            assertInstanceOf(PrinterLanguageSourceBuilder.class, client.convert().fromEpl("N"));
            assertInstanceOf(SourceBuilder.class, client.convert().fromXml("<labelZoomXml/>"));
            assertInstanceOf(DynamicSourceBuilder.class, client.convert().from(SourceFormat.ZPL, "^XA^XZ"));
            assertInstanceOf(ZplTargetBuilder.class, client.convert().fromZpl("^XA^XZ").toZpl());
            assertInstanceOf(ImageTargetBuilder.class, client.convert().fromZpl("^XA^XZ").toPng());
            assertInstanceOf(PdfTargetBuilder.class, client.convert().fromZpl("^XA^XZ").toPdf());
            assertInstanceOf(TargetBuilder.class, client.convert().fromZpl("^XA^XZ").toEpl());
        }
    }

    @Test
    void typedChainSendsTheSameParamsAsTheWire() throws Exception {
        JsonNode params = send(client -> client.convert()
                .fromPdf(new byte[] {0x25, 0x50, 0x44, 0x46})
                    .withPage(0)
                    .withSplit(s -> s.rows(2).columns(2).skipBlank(true))
                    .withRaster(r -> r.scaling(150).position(-20, 5).darkness(60).colorMode(ColorMode.BW))
                .toZpl()
                    .withDpi(203)
                    .withRotation(90)
                    .withCollapseCopies(true)
                    .withLayout(l -> l.across(2).gap(0.125f))
                .execute(),
                "/api/v2/convert/pdf/to/zpl");

        assertEquals(0, params.at("/pdf/pageNumber").asInt());
        assertEquals(2, params.at("/split/rows").asInt());
        assertEquals(2, params.at("/split/columns").asInt());
        assertTrue(params.at("/split/skipBlank").asBoolean());
        assertEquals(150.0, params.at("/scaling").asDouble(), 0.001);
        assertEquals(-20, params.at("/position/x").asInt());
        assertEquals(5, params.at("/position/y").asInt());
        assertEquals(60, params.at("/darkness").asInt());
        assertEquals("BW", params.at("/colorMode").asText());
        assertEquals(203, params.at("/targetDpi").asInt());
        assertEquals(90, params.at("/rotation").asInt());
        assertTrue(params.at("/zpl/collapseCopies").asBoolean());
        assertEquals(2, params.at("/layout/across").asInt());
        assertEquals(0.125, params.at("/layout/gap").asDouble(), 0.0001);
    }

    @Test
    void eachStageSendsItsOwnDpi() throws Exception {
        JsonNode params = send(client -> client.convert()
                .fromZpl("^XA^XZ").withDpi(300)
                .toZpl().withDpi(203)
                .execute(),
                "/api/v2/convert/zpl/to/zpl");

        assertEquals(300, params.at("/sourceDpi").asInt());
        assertEquals(203, params.at("/targetDpi").asInt());
        assertFalse(params.has("dpi"), "the legacy dpi key must never be sent (contract C8)");
    }

    @Test
    void aGroupSetAcrossBothStagesIsMergedRatherThanReplaced() throws Exception {
        JsonNode params = send(client -> client.convert()
                .fromZpl("^XA^XZ").withCommandsToIgnore("^PQ")
                .toZpl().withImageCompression(ZplImageCompression.COMPRESSED_HEX)
                .execute(),
                "/api/v2/convert/zpl/to/zpl");

        assertEquals("^PQ", params.at("/zpl/commandsToIgnore/0").asText());
        assertEquals("COMPRESSED_HEX", params.at("/zpl/imageCompression").asText());
    }

    @Test
    void rasterValidationStillNamesTheParameter() {
        try (LabelZoomClient client = LabelZoomClient.builder().apiKey(null).build()) {
            LabelZoomValidationException ex = assertThrows(LabelZoomValidationException.class,
                    () -> client.convert().fromPng(new byte[] {1}).withRaster(r -> r.darkness(101)));
            assertEquals("darkness", ex.parameter());
        }
    }

    private interface Call {
        ConversionResult run(LabelZoomClient client);
    }

    private static JsonNode send(Call call, String expectedPath) throws Exception {
        RecordingTransport transport = new RecordingTransport();
        transport.enqueue(RecordingTransport.response(200, "^XA^XZ", Map.of()));
        try (LabelZoomClient client = LabelZoomClient.builder()
                .apiKey(null).maxRetries(0).transport(transport).build()) {
            call.run(client);
        }
        URI uri = URI.create(transport.lastRequest().uri());
        assertEquals(expectedPath, uri.getPath());
        String query = uri.getRawQuery();
        assertTrue(query.startsWith("params="), query);
        return MAPPER.readTree(URLDecoder.decode(query.substring("params=".length()), StandardCharsets.UTF_8));
    }

    private static Class<?> type(String simpleName) throws ClassNotFoundException {
        return Class.forName("com.labelzoom.sdk." + simpleName);
    }

    private static boolean hasMethod(Class<?> type, String name) {
        for (Method method : type.getMethods()) {
            if (method.getName().equals(name) && Modifier.isPublic(method.getModifiers())) {
                return true;
            }
        }
        return false;
    }
}
