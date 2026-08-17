package com.labelzoom.sdk;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertInstanceOf;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;
import static org.junit.jupiter.api.Assertions.fail;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import java.io.IOException;
import java.io.UncheckedIOException;
import java.net.URI;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.Base64;
import java.util.HashMap;
import java.util.HashSet;
import java.util.Iterator;
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
 * Runs the shared conformance fixtures against the Java SDK.
 *
 * <p>Entirely offline — every case is served by {@link RecordingTransport}. No credential, no
 * network, so this passes identically on a fork pull request. The same fixtures drive the .NET and
 * Node suites; see {@code docs/CONFORMANCE.md}.
 */
class ConformanceTest {

    private static final String LANGUAGE = "java";
    private static final ObjectMapper MAPPER = new ObjectMapper();
    private static final Path ROOT = findConformanceRoot();

    private static final List<String> ALL_CASE_IDS = loadSpecCaseIds();
    private static final Map<String, String> SKIPS = loadSkips();

    static Stream<String> expectedCaseIds() {
        return ALL_CASE_IDS.stream().filter(id -> !SKIPS.containsKey(id));
    }

    @ParameterizedTest(name = "{0}")
    @MethodSource("expectedCaseIds")
    void conformanceCase(String caseId) throws Exception {
        JsonNode fixture = read(ROOT.resolve("cases").resolve(caseId + ".json"));
        JsonNode given = fixture.get("given");
        JsonNode expect = fixture.get("expect");

        switch (caseId.split("/")[0]) {
            case "request" -> runRequestCase(given, expect);
            case "response" -> runResponseCase(given, expect);
            case "retry" -> runRetryCase(given, expect);
            case "validation" -> runValidationCase(given, expect);
            case "typecheck" -> runTypecheckCase(given);
            default -> fail("Unknown case kind for " + caseId);
        }
    }

    /**
     * Asserts this suite covers every case the contract declares.
     *
     * <p>The whole anti-drift mechanism. A suite that quietly runs a subset reports success exactly
     * like one that runs all of them, so coverage is asserted rather than assumed.
     */
    @Test
    void suiteCoversEveryDeclaredCase() {
        Set<String> all = new HashSet<>(ALL_CASE_IDS);
        for (Map.Entry<String, String> skip : SKIPS.entrySet()) {
            assertTrue(all.contains(skip.getKey()),
                    "skips/" + LANGUAGE + ".json declares unknown case " + skip.getKey());
            assertFalse(skip.getValue().isBlank(), "Skip " + skip.getKey() + " has no reason");
        }
        Set<String> expected = new TreeSet<>(all);
        expected.removeAll(SKIPS.keySet());
        assertEquals(expected, new TreeSet<>(expectedCaseIds().collect(Collectors.toList())));
        // Java declares exactly one skip, and it is a compile-time-stronger guarantee rather
        // than missing coverage. Pinning the set here stops a future skip being added quietly.
        assertEquals(Set.of("validation/data-element-not-an-object"), SKIPS.keySet());
    }

    // ------------------------------------------------------------------ request

    private void runRequestCase(JsonNode given, JsonNode expect) {
        RecordingTransport transport = new RecordingTransport();
        transport.enqueue(RecordingTransport.response(
                200, "^XA^XZ", Map.of("Content-Type", List.of("text/plain"))));

        LabelZoomClient client = newClient(given, transport, null, null);
        buildRequest(client, given).execute();

        HttpTransport.Request request = transport.lastRequest();
        URI uri = URI.create(request.uri());

        if (expect.has("method")) {
            assertEquals(expect.get("method").asText(), request.method());
        }
        if (expect.has("url")) {
            assertEquals(expect.get("url").asText(),
                    uri.getScheme() + "://" + uri.getAuthority() + uri.getPath());
        }
        if (expect.has("path")) {
            assertEquals(expect.get("path").asText(), uri.getPath());
        }

        Map<String, String> headers = lowercaseHeaders(request.headers());
        if (expect.has("headers")) {
            expect.get("headers").fields().forEachRemaining(e ->
                    assertEquals(e.getValue().asText(), headers.get(e.getKey().toLowerCase()),
                            "header " + e.getKey()));
        }
        for (JsonNode absent : optionalArray(expect, "headersAbsent")) {
            assertFalse(headers.containsKey(absent.asText().toLowerCase()),
                    "header " + absent.asText() + " must be absent");
        }
        if (expect.has("headersMatch")) {
            expect.get("headersMatch").fields().forEachRemaining(e -> {
                String value = headers.get(e.getKey().toLowerCase());
                assertNotNull(value, "header " + e.getKey());
                assertTrue(java.util.regex.Pattern.compile(e.getValue().asText()).matcher(value).find(),
                        "header " + e.getKey() + " = " + value + " should match " + e.getValue().asText());
            });
        }
        if (expect.has("headersNotMatch")) {
            expect.get("headersNotMatch").fields().forEachRemaining(e -> {
                String value = headers.get(e.getKey().toLowerCase());
                if (value != null) {
                    assertFalse(java.util.regex.Pattern.compile(e.getValue().asText()).matcher(value).find(),
                            "header " + e.getKey() + " must not match " + e.getValue().asText());
                }
            });
        }

        Map<String, String> query = parseQuery(uri.getRawQuery());
        if (expect.has("queryJson")) {
            Iterator<Map.Entry<String, JsonNode>> it = expect.get("queryJson").fields();
            while (it.hasNext()) {
                Map.Entry<String, JsonNode> entry = it.next();
                String raw = query.get(entry.getKey());
                assertNotNull(raw, "query parameter " + entry.getKey());
                // Structural, not textual: JSON key order differs per language and
                // percent-encoding differs per stdlib, so comparing encoded strings would be
                // flake by construction.
                assertJsonEquivalent(entry.getValue(), parse(raw), entry.getKey());
            }
        }
        for (JsonNode absent : optionalArray(expect, "queryAbsent")) {
            assertNull(query.get(absent.asText()), "query parameter " + absent.asText());
        }
        if (expect.has("queryJsonAbsentKeys")) {
            Iterator<Map.Entry<String, JsonNode>> it = expect.get("queryJsonAbsentKeys").fields();
            while (it.hasNext()) {
                Map.Entry<String, JsonNode> entry = it.next();
                JsonNode actual = parse(query.get(entry.getKey()));
                for (JsonNode key : entry.getValue()) {
                    assertFalse(actual.has(key.asText()),
                            entry.getKey() + " must not contain " + key.asText()
                                    + " -- only options the caller explicitly set may be serialized");
                }
            }
        }
        if (expect.has("bodyText")) {
            assertEquals(expect.get("bodyText").asText(),
                    new String(request.body(), StandardCharsets.UTF_8));
        }
    }

    // ----------------------------------------------------------------- response

    private void runResponseCase(JsonNode given, JsonNode expect) {
        RecordingTransport transport = new RecordingTransport();
        enqueueScripted(transport, given);

        // Response cases queue one response and assert how it maps. Retry is the subject of
        // retry/*, and leaving it on would consume responses that do not exist for 429 and 5xx.
        LabelZoomClient client = newClient(given, transport, null, 0);

        if (expect.has("error")) {
            LabelZoomException error = assertThrows(LabelZoomException.class,
                    () -> client.convert().fromZpl("^XA^XZ").toZpl().execute());
            assertError(expect.get("error"), error);
            return;
        }

        ConversionResult result = client.convert().fromZpl("^XA^XZ").toZpl().execute();
        JsonNode r = expect.get("result");
        if (r.has("status")) {
            assertEquals(r.get("status").asInt(), result.statusCode());
        }
        if (r.has("contentType")) {
            assertEquals(r.get("contentType").asText(), result.contentType().orElse(null));
        }
        if (r.has("text")) {
            assertEquals(r.get("text").asText(), result.text());
        }
        if (r.has("bytesBase64")) {
            assertEquals(r.get("bytesBase64").asText(),
                    Base64.getEncoder().encodeToString(result.bytes()));
        }
        if (r.has("requestId")) {
            JsonNode expected = r.get("requestId");
            assertEquals(expected.isNull() ? null : expected.asText(), result.requestId().orElse(null));
        }
    }

    // -------------------------------------------------------------------- retry

    private void runRetryCase(JsonNode given, JsonNode expect) {
        RecordingTransport transport = new RecordingTransport();
        enqueueScripted(transport, given);

        List<Double> sleeps = new ArrayList<>();
        LabelZoomClient client = newClient(given, transport, millis -> sleeps.add(millis / 1000.0), null);

        if (expect.has("error")) {
            LabelZoomException error = assertThrows(LabelZoomException.class,
                    () -> client.convert().fromZpl("^XA^XZ").toZpl().execute());
            assertError(expect.get("error"), error);
        } else {
            ConversionResult result = client.convert().fromZpl("^XA^XZ").toZpl().execute();
            if (expect.get("result").has("text")) {
                assertEquals(expect.get("result").get("text").asText(), result.text());
            }
        }

        assertEquals(expect.get("attempts").asInt(), transport.requests.size(), "attempts");

        List<Double> expectedSleeps = new ArrayList<>();
        expect.get("sleepsSeconds").forEach(n -> expectedSleeps.add(n.asDouble()));
        assertEquals(expectedSleeps, sleeps, "sleeps");
    }

    // --------------------------------------------------------------- validation

    private void runValidationCase(JsonNode given, JsonNode expect) {
        RecordingTransport transport = new RecordingTransport();
        LabelZoomClient client = newClient(given, transport, null, null);

        LabelZoomValidationException thrown = assertThrows(LabelZoomValidationException.class,
                () -> buildRequest(client, given).execute());

        assertEquals(expect.get("validationError").get("parameter").asText(), thrown.parameter());
        // Local validation must never reach the network.
        assertEquals(expect.get("requestsSent").asInt(), transport.requests.size());
    }

    // ---------------------------------------------------------------- typecheck

    /**
     * The runtime stand-in for "this snippet must not compile".
     *
     * <p>A test cannot assert a compile error about itself, so it asserts the property that makes
     * the compile error inevitable: {@link TargetFormat} has no constant for any source-only
     * format, and it is a different type from {@link SourceFormat}.
     */
    private void runTypecheckCase(JsonNode given) {
        String snippet = given.get("snippet").asText();
        Set<String> targetNames = java.util.Arrays.stream(TargetFormat.values())
                .map(Enum::name).collect(Collectors.toSet());

        for (String sourceOnly : List.of("EPL", "TSPL", "DPL")) {
            if (snippet.toUpperCase().contains("TO(" + sourceOnly + ")")) {
                assertFalse(targetNames.contains(sourceOnly),
                        sourceOnly + " is source-only and must not exist as a TargetFormat");
            }
        }
        if (snippet.contains("SourceFormat.")) {
            assertFalse(TargetFormat.class.isAssignableFrom(SourceFormat.class),
                    "SourceFormat must not be usable where a TargetFormat is expected");
        }
    }

    // ------------------------------------------------------------------ helpers

    private static LabelZoomClient newClient(
            JsonNode given, HttpTransport transport,
            java.util.function.LongConsumer onSleep, Integer defaultMaxRetries) {

        LabelZoomClient.Builder builder = LabelZoomClient.builder()
                .transport(transport)
                // Deterministic backoff: the fixtures assert exact sleep durations.
                .useJitter(false)
                .sleeper(onSleep != null ? onSleep : millis -> { });

        // Fixtures that exercise the LABELZOOM_API_KEY fallback supply given.env. Always install
        // a lookup, so a real key in the developer's shell can never change a result.
        Map<String, String> environment = new LinkedHashMap<>();
        if (given.has("env")) {
            given.get("env").fields().forEachRemaining(e -> environment.put(e.getKey(), e.getValue().asText()));
        }
        builder.environmentLookup(environment::get);

        JsonNode client = given.get("client");
        if (client != null && client.has("apiKey")) {
            JsonNode key = client.get("apiKey");
            builder.apiKey(key.isNull() ? null : key.asText());
        } else if (client == null) {
            // Cases that do not configure a client must still not pick up a real key from the
            // developer's environment.
            builder.apiKey("");
        }
        if (client != null && client.has("baseUrl")) {
            builder.baseUrl(client.get("baseUrl").asText());
        }
        if (given.has("maxRetries")) {
            builder.maxRetries(given.get("maxRetries").asInt());
        } else if (defaultMaxRetries != null) {
            builder.maxRetries(defaultMaxRetries);
        }
        return builder.build();
    }

    /** Translates the fixture's wire-shaped options into fluent SDK calls. */
    private static ConversionTargetBuilder buildRequest(LabelZoomClient client, JsonNode given) {
        SourceFormat source = SourceFormat.fromToken(given.get("source").asText());
        TargetFormat target = TargetFormat.fromToken(given.get("target").asText());
        String body = given.get("bodyText").asText();

        boolean base64Text = given.has("sourceEncoding")
                && "base64text".equals(given.get("sourceEncoding").asText());

        ConversionTargetBuilder builder = base64Text
                ? client.convert().fromBase64Text(source, body).to(target)
                : client.convert().from(source, body).to(target);

        if (!given.has("options")) {
            return builder;
        }

        Iterator<Map.Entry<String, JsonNode>> it = given.get("options").fields();
        while (it.hasNext()) {
            Map.Entry<String, JsonNode> option = it.next();
            JsonNode v = option.getValue();
            switch (option.getKey()) {
                case "dpi" -> builder.withDpi(v.asInt());
                case "rotation" -> builder.withRotation(v.asInt());
                case "scaling" -> builder.withScaling((float) v.asDouble());
                case "colorMode" -> builder.withColorMode(ColorMode.valueOf(v.asText()));
                case "darkness" -> builder.withDarkness(v.asInt());
                case "watermark" -> builder.withWatermark(v.asBoolean());
                case "dialect" -> builder.withDialect(v.asText());
                case "position" -> builder.withPosition(v.get("x").asInt(), v.get("y").asInt());
                case "label" -> builder.withLabelSize(
                        (float) v.get("width").asDouble(), (float) v.get("height").asDouble());
                case "pdf" -> {
                    if (v.has("conversionMode")) {
                        builder.withPdfConversionMode(
                                PdfConversionMode.valueOf(v.get("conversionMode").asText()));
                    }
                    if (v.has("pageNumber")) {
                        builder.withPdfPage(v.get("pageNumber").asInt());
                    }
                }
                case "zpl" -> {
                    if (v.has("commandsToIgnore")) {
                        List<String> commands = new ArrayList<>();
                        v.get("commandsToIgnore").forEach(n -> commands.add(n.asText()));
                        builder.withZplCommandsToIgnore(commands.toArray(new String[0]));
                    }
                    if (v.has("imageCompression")) {
                        builder.withZplImageCompression(
                                ZplImageCompression.valueOf(v.get("imageCompression").asText()));
                    }
                }
                case "data" -> {
                    List<Map<String, Object>> records = new ArrayList<>();
                    // A bare object means one label, so it is wrapped rather than rejected.
                    if (v.isArray()) {
                        v.forEach(n -> records.add(toMap(n)));
                    } else {
                        records.add(toMap(v));
                    }
                    builder.withData(records);
                }
                default -> throw new IllegalStateException(
                        "Fixture sets option '" + option.getKey() + "', which the Java runner does "
                                + "not map. Add it to buildRequest rather than skipping the case.");
            }
        }
        return builder;
    }

    @SuppressWarnings("unchecked")
    private static Map<String, Object> toMap(JsonNode node) {
        return MAPPER.convertValue(node, Map.class);
    }

    private static void enqueueScripted(RecordingTransport transport, JsonNode given) {
        if (given.has("responses")) {
            given.get("responses").forEach(r -> enqueueOne(transport, r));
        } else {
            enqueueOne(transport, given);
        }
    }

    private static void enqueueOne(RecordingTransport transport, JsonNode node) {
        if (node.has("transportError")) {
            transport.enqueueTransportError();
            return;
        }
        Map<String, List<String>> headers = new LinkedHashMap<>();
        if (node.has("headers")) {
            node.get("headers").fields().forEachRemaining(e ->
                    headers.put(e.getKey(), List.of(e.getValue().asText())));
        }
        byte[] body;
        if (node.has("bodyBase64")) {
            body = Base64.getDecoder().decode(node.get("bodyBase64").asText());
        } else if (node.has("bodyTextRepeat")) {
            JsonNode repeat = node.get("bodyTextRepeat");
            body = repeat.get(0).asText().repeat(repeat.get(1).asInt())
                    .getBytes(StandardCharsets.UTF_8);
        } else {
            body = node.get("bodyText").asText().getBytes(StandardCharsets.UTF_8);
        }
        transport.enqueue(new HttpTransport.Response(node.get("status").asInt(), headers, body));
    }

    private static void assertError(JsonNode expected, LabelZoomException actual) {
        if (expected.has("kind")) {
            assertInstanceOf(exceptionTypeFor(expected.get("kind").asText()), actual);
        }
        if (expected.has("status")) {
            assertEquals(expected.get("status").asInt(), actual.statusCode());
        }
        if (expected.has("message")) {
            assertEquals(expected.get("message").asText(), actual.getMessage());
        }
        if (expected.has("messageNonEmpty")) {
            assertFalse(actual.getMessage().isBlank());
        }
        if (expected.has("messageMaxLength")) {
            assertTrue(actual.getMessage().length() <= expected.get("messageMaxLength").asInt(),
                    "message is " + actual.getMessage().length() + " chars");
        }
        if (expected.has("rawBodyLength")) {
            assertEquals(expected.get("rawBodyLength").asInt(), actual.rawBody().orElse("").length());
        }
        if (expected.has("rawBodyPresent") && expected.get("rawBodyPresent").asBoolean()) {
            assertFalse(actual.rawBody().orElse("").isEmpty());
        }
        if (expected.has("requestId")) {
            assertEquals(expected.get("requestId").asText(), actual.requestId().orElse(null));
        }
        if (expected.has("isPaidFeature")) {
            LabelZoomForbiddenException forbidden = assertInstanceOf(
                    LabelZoomForbiddenException.class, actual);
            assertEquals(expected.get("isPaidFeature").asBoolean(), forbidden.isPaidFeature());
        }
        if (expected.has("retryAfterSeconds")) {
            LabelZoomRateLimitedException limited = assertInstanceOf(
                    LabelZoomRateLimitedException.class, actual);
            assertEquals(expected.get("retryAfterSeconds").asInt(),
                    limited.retryAfterSeconds().orElse(-1));
        }
    }

    private static Class<? extends LabelZoomException> exceptionTypeFor(String kind) {
        return switch (kind) {
            case "BadRequest" -> LabelZoomBadRequestException.class;
            case "Unauthorized" -> LabelZoomUnauthorizedException.class;
            case "Forbidden" -> LabelZoomForbiddenException.class;
            case "NotFound" -> LabelZoomNotFoundException.class;
            case "PayloadTooLarge" -> LabelZoomPayloadTooLargeException.class;
            case "RateLimited" -> LabelZoomRateLimitedException.class;
            case "ServerError" -> LabelZoomServerException.class;
            default -> throw new IllegalArgumentException("Unknown error kind " + kind);
        };
    }

    /**
     * Structural JSON comparison.
     *
     * <p>Property order is not guaranteed and differs across the seven SDK languages, and numbers
     * round-trip differently (4 vs 4.0), so both are normalized rather than compared textually.
     */
    private static void assertJsonEquivalent(JsonNode expected, JsonNode actual, String path) {
        if (expected.isObject()) {
            assertTrue(actual.isObject(), path + ": expected an object");
            assertEquals(expected.size(), actual.size(), path + ": property count");
            expected.fields().forEachRemaining(e -> {
                assertTrue(actual.has(e.getKey()), path + "." + e.getKey() + " is missing");
                assertJsonEquivalent(e.getValue(), actual.get(e.getKey()), path + "." + e.getKey());
            });
        } else if (expected.isArray()) {
            assertTrue(actual.isArray(), path + ": expected an array");
            assertEquals(expected.size(), actual.size(), path + ": length");
            for (int i = 0; i < expected.size(); i++) {
                assertJsonEquivalent(expected.get(i), actual.get(i), path + "[" + i + "]");
            }
        } else if (expected.isNumber()) {
            assertTrue(actual.isNumber(), path + ": expected a number");
            assertEquals(expected.asDouble(), actual.asDouble(), 1e-6, path);
        } else {
            assertEquals(expected.getNodeType(), actual.getNodeType(), path + ": node type");
            assertEquals(expected.asText(), actual.asText(), path);
        }
    }

    private static Map<String, String> lowercaseHeaders(Map<String, String> headers) {
        Map<String, String> out = new HashMap<>();
        headers.forEach((k, v) -> out.put(k.toLowerCase(), v));
        return out;
    }

    private static Map<String, String> parseQuery(String rawQuery) {
        Map<String, String> out = new LinkedHashMap<>();
        if (rawQuery == null || rawQuery.isEmpty()) {
            return out;
        }
        for (String pair : rawQuery.split("&")) {
            int eq = pair.indexOf('=');
            String name = eq < 0 ? pair : pair.substring(0, eq);
            String value = eq < 0 ? "" : pair.substring(eq + 1);
            out.put(java.net.URLDecoder.decode(name, StandardCharsets.UTF_8),
                    java.net.URLDecoder.decode(value, StandardCharsets.UTF_8));
        }
        return out;
    }

    private static JsonNode parse(String json) {
        try {
            return MAPPER.readTree(json);
        } catch (IOException e) {
            throw new UncheckedIOException(e);
        }
    }

    private static JsonNode read(Path path) {
        try {
            return MAPPER.readTree(Files.readString(path));
        } catch (IOException e) {
            throw new UncheckedIOException("Could not read " + path, e);
        }
    }

    private static Iterable<JsonNode> optionalArray(JsonNode node, String field) {
        return node.has(field) ? node.get(field) : List.of();
    }

    private static List<String> loadSpecCaseIds() {
        List<String> ids = new ArrayList<>();
        read(ROOT.resolve("spec.json")).get("cases").forEach(n -> ids.add(n.asText()));
        return ids;
    }

    private static Map<String, String> loadSkips() {
        Path path = ROOT.resolve("skips").resolve(LANGUAGE + ".json");
        Map<String, String> skips = new LinkedHashMap<>();
        if (Files.exists(path)) {
            read(path).get("skips").forEach(n ->
                    skips.put(n.get("id").asText(), n.get("reason").asText("")));
        }
        return skips;
    }

    private static Path findConformanceRoot() {
        Path directory = Path.of("").toAbsolutePath();
        for (int i = 0; i < 6 && directory != null; i++) {
            Path candidate = directory.resolve("conformance");
            if (Files.isDirectory(candidate.resolve("cases"))) {
                return candidate;
            }
            directory = directory.getParent();
        }
        throw new IllegalStateException("Could not locate the conformance/ directory.");
    }
}
