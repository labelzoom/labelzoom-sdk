package com.labelzoom.sdk;

import java.io.IOException;
import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.time.Duration;
import java.util.Map;

/**
 * The default {@link HttpTransport}, backed by {@code java.net.http.HttpClient}.
 *
 * <p>This is the only class in the SDK that may reference {@code java.net.http}. Android has no
 * {@code java.net.http}, so an Android caller supplies its own transport, and this class must
 * then never be loaded. {@code AndroidCompatibilityTest} pins that.
 */
final class JdkHttpTransport implements HttpTransport {

    /**
     * Builds the transport a client gets when the caller supplies none. Returns the interface so
     * that verifying the caller never needs this class's {@code java.net.http} types.
     */
    static HttpTransport createDefault(Duration timeout) {
        return new JdkHttpTransport(
                HttpClient.newBuilder().connectTimeout(Duration.ofSeconds(10)).build(), timeout);
    }

    private final HttpClient httpClient;
    private final Duration timeout;

    JdkHttpTransport(HttpClient httpClient, Duration timeout) {
        this.httpClient = httpClient;
        this.timeout = timeout;
    }

    @Override
    public Response send(Request request) throws IOException, InterruptedException {
        HttpRequest.Builder builder = HttpRequest.newBuilder(URI.create(request.uri()))
                .POST(HttpRequest.BodyPublishers.ofByteArray(request.body()));

        for (Map.Entry<String, String> header : request.headers().entrySet()) {
            builder.header(header.getKey(), header.getValue());
        }
        if (timeout != null) {
            builder.timeout(timeout);
        }

        HttpResponse<byte[]> response =
                httpClient.send(builder.build(), HttpResponse.BodyHandlers.ofByteArray());

        return new Response(response.statusCode(), response.headers().map(), response.body());
    }
}
