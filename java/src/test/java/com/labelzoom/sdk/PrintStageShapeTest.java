package com.labelzoom.sdk;

import static org.junit.jupiter.api.Assertions.assertArrayEquals;
import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertInstanceOf;
import static org.junit.jupiter.api.Assertions.assertNotEquals;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import java.lang.reflect.Method;
import java.lang.reflect.Modifier;
import java.net.URI;
import java.net.URLDecoder;
import java.nio.charset.StandardCharsets;
import java.util.Arrays;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.TreeSet;
import java.util.stream.Collectors;
import java.util.stream.Stream;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.MethodSource;

/**
 * Pins the printer target (contract section 1 and P2): a printer is reached from every source stage
 * through {@code toPrinter}, and its stage offers only the options that apply to a printer.
 */
class PrintStageShapeTest {

    private static final ObjectMapper MAPPER = new ObjectMapper();

    private static final Map<String, List<String>> JSON = Map.of("Content-Type", List.of("application/json"));

    static Stream<Class<?>> sourceStages() {
        return Stream.of(
                SourceBuilder.class, PrinterLanguageSourceBuilder.class, ZplSourceBuilder.class,
                ImageSourceBuilder.class, PdfSourceBuilder.class, DynamicSourceBuilder.class);
    }

    @ParameterizedTest
    @MethodSource("sourceStages")
    void everySourceStageCanTargetAPrinter(Class<?> stage) throws Exception {
        Method toPrinter = stage.getMethod("toPrinter", String.class);
        assertEquals(PrinterTargetBuilder.class, toPrinter.getReturnType());
    }

    @Test
    void thePrinterTargetOffersOnlyPrinterOptions() throws Exception {
        assertEquals(Set.of("execute", "withCopies", "withIdempotencyKey", "withRotation"),
                options(PrinterTargetBuilder.class));
        assertEquals(PrintJob.class, PrinterTargetBuilder.class.getMethod("execute").getReturnType());
    }

    @Test
    void theTemplateSourceTakesDataAndTargetsOnlyAPrinter() throws Exception {
        assertEquals(Set.of("toPrinter", "withData", "withValidate"), options(TemplateSourceBuilder.class));
        assertEquals(Set.of("execute", "withCopies", "withIdempotencyKey"),
                options(TemplatePrinterTargetBuilder.class));
        assertEquals(PrintBatch.class, TemplatePrinterTargetBuilder.class.getMethod("execute").getReturnType());
        try (LabelZoomClient client = LabelZoomClient.builder().apiKey(null).build()) {
            assertInstanceOf(TemplatePrinterTargetBuilder.class,
                    client.convert().fromTemplate("template-1").toPrinter("printer-1"));
        }
    }

    @Test
    void theClientHasNoSeparatePrintEntry() {
        assertFalse(options(LabelZoomClient.class).contains("print"),
                "a printer is a target on the convert chain, not a separate entry point");
    }

    @Test
    void aPrintRefusesAUrlAndBase64Text() {
        try (LabelZoomClient client = LabelZoomClient.builder().apiKey("lz_test").build()) {
            LabelZoomValidationException url = assertThrows(LabelZoomValidationException.class,
                    () -> client.convert().fromUrl("https://example.com/label.zpl").toPrinter("p1"));
            assertEquals("source", url.parameter());

            LabelZoomValidationException base64 = assertThrows(LabelZoomValidationException.class,
                    () -> client.convert().fromBase64Text(SourceFormat.PDF, "JVBERi0=").toPrinter("p1"));
            assertEquals("source", base64.parameter());

            LabelZoomValidationException printer = assertThrows(LabelZoomValidationException.class,
                    () -> client.convert().fromZpl("^XA^XZ").toPrinter(" "));
            assertEquals("printerId", printer.parameter());
        }
    }

    @Test
    void typedChainSendsSourceOptionsRotationCopiesAndKey() throws Exception {
        RecordingTransport transport = new RecordingTransport();
        transport.enqueue(RecordingTransport.response(
                200, "{\"jobId\":\"j1\",\"status\":\"queued\",\"copies\":2}", JSON));

        PrintJob job;
        try (LabelZoomClient client = LabelZoomClient.builder()
                .apiKey("lz_test").maxRetries(0).transport(transport).build()) {
            job = client.convert()
                    .fromPdf(new byte[] {0x25, 0x50, 0x44, 0x46})
                        .withPage(1)
                        .withSplit(s -> s.rows(2))
                    .toPrinter("printer/1")
                        .withRotation(90)
                        .withCopies(2)
                        .withIdempotencyKey("order-42")
                    .execute();
        }

        HttpTransport.Request request = transport.lastRequest();
        URI uri = URI.create(request.uri());
        assertEquals("POST", request.method());
        // The id is one path segment: its slash is encoded rather than routing elsewhere.
        assertEquals("/api/v3/printers/printer%2F1/print", uri.getRawPath());
        Map<String, String> query = query(uri.getRawQuery());
        assertEquals("pdf", query.get("sourceFormat"));
        assertEquals("2", query.get("copies"));
        JsonNode params = MAPPER.readTree(query.get("params"));
        assertEquals(1, params.at("/pdf/pageNumber").asInt());
        assertEquals(2, params.at("/split/rows").asInt());
        assertEquals(90, params.at("/rotation").asInt());
        assertEquals("order-42", request.headers().get("Idempotency-Key"));
        assertEquals("application/pdf", request.headers().get("Content-Type"));
        assertEquals("application/json", request.headers().get("Accept"));

        assertEquals("j1", job.jobId());
        assertEquals("queued", job.status());
        assertEquals(2, job.copies().getAsInt());
        assertFalse(job.isIdempotent());
    }

    @Test
    void aDesignFilePrintsAsItsOwnBytes() {
        RecordingTransport transport = new RecordingTransport();
        transport.enqueue(RecordingTransport.response(200, "{\"jobId\":\"j1\",\"status\":\"queued\"}", JSON));
        byte[] btw = {0x00, (byte) 0xff, 0x10};

        try (LabelZoomClient client = LabelZoomClient.builder()
                .apiKey("lz_test").maxRetries(0).transport(transport).build()) {
            client.convert().fromBtw(btw).toPrinter("p1").execute();
        }

        HttpTransport.Request request = transport.lastRequest();
        Map<String, String> query = query(URI.create(request.uri()).getRawQuery());
        assertEquals("btw", query.get("sourceFormat"));
        assertFalse(query.containsKey("params"), "no option was set, so no params");
        assertEquals("application/octet-stream", request.headers().get("Content-Type"));
        assertArrayEquals(btw, request.body());
    }

    @Test
    void eachCallGetsItsOwnGeneratedKey() {
        RecordingTransport transport = new RecordingTransport();
        transport.enqueue(RecordingTransport.response(200, "{\"jobId\":\"j1\",\"status\":\"queued\"}", JSON));
        transport.enqueue(RecordingTransport.response(200, "{\"jobId\":\"j2\",\"status\":\"queued\"}", JSON));

        try (LabelZoomClient client = LabelZoomClient.builder()
                .apiKey("lz_test").maxRetries(0).transport(transport).build()) {
            PrinterTargetBuilder print = client.convert().fromZpl("^XA^XZ").toPrinter("printer-1");
            print.execute();
            print.execute();
        }

        // Two calls are two prints. Only a retry within one call may reuse a key.
        String first = transport.requests.get(0).headers().get("Idempotency-Key");
        String second = transport.requests.get(1).headers().get("Idempotency-Key");
        assertTrue(first.matches("[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}"), first);
        assertNotEquals(first, second);
    }

    @Test
    void aQuotaErrorIsARateLimitError() {
        RecordingTransport transport = new RecordingTransport();
        transport.enqueue(RecordingTransport.response(
                429, "{\"message\":\"Daily print job limit of 1000 reached.\",\"code\":\"daily_job_limit_reached\"}",
                Map.of("Retry-After", List.of("14400"))));

        try (LabelZoomClient client = LabelZoomClient.builder()
                .apiKey("lz_test").transport(transport).sleeper(millis -> { }).build()) {
            LabelZoomRateLimitedException error = assertThrows(
                    LabelZoomRateLimitedException.class,
                    () -> client.convert().fromZpl("^XA^XZ").toPrinter("printer-1").execute());
            assertInstanceOf(LabelZoomPrintQuotaExceededException.class, error);
            assertEquals("daily_job_limit_reached", error.code().orElse(null));
        }
        // Retries are on by default here, and the daily limit still isn't retried (contract F5).
        assertEquals(1, transport.requests.size());
    }

    private static Set<String> options(Class<?> type) {
        return Arrays.stream(type.getMethods())
                .filter(m -> m.getDeclaringClass() != Object.class)
                .filter(m -> Modifier.isPublic(m.getModifiers()) && !Modifier.isStatic(m.getModifiers()))
                .map(Method::getName)
                .collect(Collectors.toCollection(TreeSet::new));
    }

    private static Map<String, String> query(String rawQuery) {
        Map<String, String> out = new LinkedHashMap<>();
        for (String pair : rawQuery.split("&")) {
            int eq = pair.indexOf('=');
            out.put(URLDecoder.decode(pair.substring(0, eq), StandardCharsets.UTF_8),
                    URLDecoder.decode(pair.substring(eq + 1), StandardCharsets.UTF_8));
        }
        return out;
    }
}
