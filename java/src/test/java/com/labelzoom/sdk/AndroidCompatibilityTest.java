package com.labelzoom.sdk;

import static org.junit.jupiter.api.Assertions.assertArrayEquals;
import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.io.ByteArrayInputStream;
import java.io.IOException;
import java.net.URISyntaxException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.stream.Stream;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.ValueSource;

/**
 * Keeps the SDK usable on Android, which has no {@code java.net.http}.
 *
 * <p>An Android caller passes its own {@link HttpTransport}. That works only while
 * {@link JdkHttpTransport} is the sole class that touches {@code java.net.http}: a reference from
 * anywhere else, such as building the default client inline in a constructor, is loaded on every
 * code path. The build's Animal Sniffer check covers the rest of the Android API surface; it has
 * to ignore {@code java.net.http} wholesale, which is why this test pins where it may appear.
 */
class AndroidCompatibilityTest {

    @Test
    void onlyTheJdkTransportReferencesJavaNetHttp() throws IOException, URISyntaxException {
        Path classes = Path.of(LabelZoomClient.class.getProtectionDomain()
                .getCodeSource().getLocation().toURI());
        List<String> offenders = new ArrayList<>();
        int scanned = 0;

        try (Stream<Path> files = Files.walk(classes)) {
            for (Path file : (Iterable<Path>) files.filter(p -> p.toString().endsWith(".class"))::iterator) {
                scanned++;
                String name = classes.relativize(file).toString();
                boolean allowed = file.getFileName().toString().startsWith("JdkHttpTransport");
                // Class-file constant pools hold names as modified UTF-8, so a byte search for the
                // internal name is exact.
                String pool = new String(Files.readAllBytes(file), StandardCharsets.ISO_8859_1);
                if (!allowed && pool.contains("java/net/http")) {
                    offenders.add(name);
                }
            }
        }

        assertTrue(scanned > 10, "expected to scan the SDK's compiled classes, found " + scanned);
        assertEquals(List.of(), offenders,
                "only JdkHttpTransport may reference java.net.http; Android has none");
    }

    @Test
    void shipsTheR8RuleForTheMissingJavaNetHttpClasses() throws IOException {
        var rule = LabelZoomClient.class.getResourceAsStream("/META-INF/proguard/labelzoom-sdk.pro");
        assertNotNull(rule, "R8 reads consumer rules from META-INF/proguard/ in the jar");
        try (rule) {
            String text = new String(rule.readAllBytes(), StandardCharsets.UTF_8);
            assertTrue(text.contains("-dontwarn java.net.http.**"));
        }
    }

    @Test
    void aSuppliedTransportIsUsedInsteadOfTheDefault() {
        RecordingTransport transport = new RecordingTransport();
        transport.enqueue(RecordingTransport.response(200, "^XA^XZ", Map.of()));
        try (LabelZoomClient client = LabelZoomClient.builder()
                .apiKey(null).maxRetries(0).transport(transport).build()) {
            client.convert().fromZpl("^XA^XZ").toZpl().withDpi(203).execute();
        }
        assertEquals(1, transport.requests.size());
        // The percent-encoding moved off URLEncoder's Charset overload; the wire form must not.
        String uri = transport.lastRequest().uri();
        assertTrue(uri.endsWith("?params=%7B%22dpi%22%3A203%7D"), uri);
    }

    @ParameterizedTest
    @ValueSource(strings = {
        "", " ", "\t\n\r", " ", "　 ", "a", " a ", " ", "😀", " 😀 ",
    })
    void isBlankMatchesTheJdk(String value) {
        assertEquals(value.isBlank(), Compat.isBlank(value), () -> "for " + escape(value));
    }

    @Test
    void readAllBytesMatchesTheJdk() throws IOException {
        byte[] data = new byte[20_000];
        for (int i = 0; i < data.length; i++) {
            data[i] = (byte) i;
        }
        assertArrayEquals(data, Compat.readAllBytes(new ByteArrayInputStream(data)));
        assertArrayEquals(new byte[0], Compat.readAllBytes(new ByteArrayInputStream(new byte[0])));
        assertFalse(Compat.readAllBytes(new ByteArrayInputStream(data)).length == 0);
    }

    private static String escape(String value) {
        StringBuilder out = new StringBuilder();
        value.chars().forEach(c -> out.append(String.format("\\u%04X", c)));
        return out.toString();
    }
}
