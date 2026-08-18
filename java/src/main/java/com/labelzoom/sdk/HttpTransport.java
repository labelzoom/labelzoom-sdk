package com.labelzoom.sdk;

import java.io.IOException;
import java.util.List;
import java.util.Map;

/**
 * The SDK's HTTP seam.
 *
 * <p>Exists so the conformance suite can run entirely offline — no sockets, no local server, no
 * mocking framework — and so callers can route requests through their own instrumented client.
 * {@link JdkHttpTransport} is the default.
 */
public interface HttpTransport {

    /** Performs one request. Implementations must not retry; the client owns retry policy. */
    Response send(Request request) throws IOException, InterruptedException;

    /** An outgoing request. */
    record Request(String method, String uri, Map<String, String> headers, byte[] body) {
    }

    /** A response. {@code headers} keys are compared case-insensitively by {@link #header}. */
    record Response(int statusCode, Map<String, List<String>> headers, byte[] body) {

        /** Looks a header up case-insensitively, returning the first value. */
        public String header(String name) {
            for (Map.Entry<String, List<String>> entry : headers.entrySet()) {
                if (entry.getKey().equalsIgnoreCase(name) && !entry.getValue().isEmpty()) {
                    return entry.getValue().get(0);
                }
            }
            return null;
        }
    }
}
