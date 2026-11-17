package com.labelzoom.sdk;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertInstanceOf;
import static org.junit.jupiter.api.Assertions.assertNotEquals;
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
import org.junit.jupiter.params.provider.Arguments;
import org.junit.jupiter.params.provider.MethodSource;

/**
 * Pins the print stage against the convert source stage it mirrors (contract section 1.2).
 *
 * <p>The print builders can't reuse the convert source builders, because those carry the {@code to*}
 * transitions and print has no target stage. So each format has a print twin. These tests are what
 * stop the twins drifting: a source option added to {@link PdfSourceBuilder} and not to
 * {@link PdfPrintBuilder} fails here.
 */
class PrintStageShapeTest {

    private static final ObjectMapper MAPPER = new ObjectMapper();

    private static final Set<String> PRINT_ONLY = Set.of("withCopies", "withIdempotencyKey", "send");

    static Stream<Arguments> twins() {
        return Stream.of(
                Arguments.of(SourceBuilder.class, PrintBuilder.class),
                Arguments.of(PrinterLanguageSourceBuilder.class, PrinterLanguagePrintBuilder.class),
                Arguments.of(ZplSourceBuilder.class, ZplPrintBuilder.class),
                Arguments.of(ImageSourceBuilder.class, ImagePrintBuilder.class),
                Arguments.of(PdfSourceBuilder.class, PdfPrintBuilder.class),
                Arguments.of(DynamicSourceBuilder.class, DynamicPrintBuilder.class));
    }

    @ParameterizedTest
    @MethodSource("twins")
    void printTwinOffersExactlyTheConvertSourceOptions(Class<?> convert, Class<?> print) {
        Set<String> convertOptions = options(convert).stream()
                .filter(name -> !name.startsWith("to"))
                .collect(Collectors.toCollection(TreeSet::new));
        Set<String> printOptions = options(print).stream()
                .filter(name -> !PRINT_ONLY.contains(name))
                .collect(Collectors.toCollection(TreeSet::new));

        assertEquals(convertOptions, printOptions,
                print.getSimpleName() + " must offer exactly the source options of " + convert.getSimpleName());
    }

    @ParameterizedTest
    @MethodSource("twins")
    void printTwinHasNoTargetStage(Class<?> convert, Class<?> print) {
        for (String name : options(print)) {
            assertFalse(name.startsWith("to"), print.getSimpleName() + " must not offer " + name);
        }
        assertTrue(options(print).containsAll(PRINT_ONLY), print.getSimpleName() + " must offer " + PRINT_ONLY);
    }

    @Test
    void printOffersFromMethodsOnlyForPrintableFormats() {
        Set<String> methods = options(PrintRequestBuilder.class);
        for (String notPrintable : List.of("fromUrl", "fromBase64Text", "fromEscLabel", "fromEzpl",
                "fromTpcl", "fromNlbl", "fromLbl", "fromBtw", "fromDymo")) {
            assertFalse(methods.contains(notPrintable), "print must not offer " + notPrintable);
        }
        for (String printable : List.of("fromZpl", "fromEpl", "fromIpl", "fromTspl", "fromDpl", "fromSbpl",
                "fromFingerprint", "fromCpcl", "fromXml", "fromJson", "fromPdf", "fromPng", "fromBmp",
                "fromGif", "fromJpeg", "fromTemplate", "from", "fromFile")) {
            assertTrue(methods.contains(printable), "print must offer " + printable);
        }
    }

    @Test
    void namedMethodsReturnThePrintTwinForTheirFormat() {
        try (LabelZoomClient client = LabelZoomClient.builder().apiKey(null).build()) {
            PrintRequestBuilder print = client.print("printer-1");
            assertInstanceOf(ZplPrintBuilder.class, print.fromZpl("^XA^XZ"));
            assertInstanceOf(PrinterLanguagePrintBuilder.class, print.fromEpl("N"));
            assertInstanceOf(PrinterLanguagePrintBuilder.class, print.fromCpcl("! 0 200 200 1 1"));
            assertInstanceOf(PrintBuilder.class, print.fromXml("<labelZoomXml/>"));
            assertInstanceOf(PdfPrintBuilder.class, print.fromPdf(new byte[] {1}));
            assertInstanceOf(ImagePrintBuilder.class, print.fromPng(new byte[] {1}));
            assertInstanceOf(DynamicPrintBuilder.class, print.from(SourceFormat.JPG, new byte[] {1}));
            assertInstanceOf(TemplatePrintBuilder.class, print.fromTemplate("template-1"));
        }
    }

    @Test
    void typedPrintChainSendsSourceOptionsCopiesAndKey() throws Exception {
        RecordingTransport transport = new RecordingTransport();
        transport.enqueue(RecordingTransport.response(
                200, "{\"jobId\":\"j1\",\"status\":\"queued\",\"copies\":2}",
                Map.of("Content-Type", List.of("application/json"))));

        PrintJob job;
        try (LabelZoomClient client = LabelZoomClient.builder()
                .apiKey("lz_test").maxRetries(0).transport(transport).build()) {
            job = client.print("printer/1")
                    .fromPdf(new byte[] {0x25, 0x50, 0x44, 0x46})
                        .withPage(1)
                        .withSplit(s -> s.rows(2))
                        .withCopies(2)
                        .withIdempotencyKey("order-42")
                    .send();
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
        assertEquals("order-42", request.headers().get("Idempotency-Key"));
        assertEquals("application/pdf", request.headers().get("Content-Type"));
        assertEquals("application/json", request.headers().get("Accept"));

        assertEquals("j1", job.jobId());
        assertEquals("queued", job.status());
        assertEquals(2, job.copies().getAsInt());
        assertFalse(job.isIdempotent());
    }

    @Test
    void eachCallGetsItsOwnGeneratedKey() {
        RecordingTransport transport = new RecordingTransport();
        Map<String, List<String>> json = Map.of("Content-Type", List.of("application/json"));
        transport.enqueue(RecordingTransport.response(200, "{\"jobId\":\"j1\",\"status\":\"queued\"}", json));
        transport.enqueue(RecordingTransport.response(200, "{\"jobId\":\"j2\",\"status\":\"queued\"}", json));

        try (LabelZoomClient client = LabelZoomClient.builder()
                .apiKey("lz_test").maxRetries(0).transport(transport).build()) {
            ZplPrintBuilder print = client.print("printer-1").fromZpl("^XA^XZ");
            print.send();
            print.send();
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
            LabelZoomRateLimitedException error = org.junit.jupiter.api.Assertions.assertThrows(
                    LabelZoomRateLimitedException.class,
                    () -> client.print("printer-1").fromZpl("^XA^XZ").send());
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
