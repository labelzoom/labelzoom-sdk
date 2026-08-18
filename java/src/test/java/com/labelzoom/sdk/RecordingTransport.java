package com.labelzoom.sdk;

import java.io.IOException;
import java.util.ArrayDeque;
import java.util.ArrayList;
import java.util.Deque;
import java.util.List;
import java.util.Map;

/**
 * Records outgoing requests and replays a scripted sequence of responses.
 *
 * <p>No sockets, no local server, no mocking framework — the SDK exposes {@link HttpTransport}
 * precisely so the offline suite can run anywhere, including on fork pull requests with no
 * secrets configured.
 */
final class RecordingTransport implements HttpTransport {

    private final Deque<Object> scripted = new ArrayDeque<>();
    final List<Request> requests = new ArrayList<>();

    void enqueue(Response response) {
        scripted.add(response);
    }

    void enqueueTransportError() {
        scripted.add(new IOException("simulated transport failure"));
    }

    Request lastRequest() {
        return requests.get(requests.size() - 1);
    }

    @Override
    public Response send(Request request) throws IOException {
        requests.add(request);
        Object next = scripted.poll();
        if (next == null) {
            throw new IllegalStateException(
                    "Unexpected request to " + request.uri() + "; no more responses are scripted.");
        }
        if (next instanceof IOException e) {
            throw e;
        }
        return (Response) next;
    }

    static Response response(int status, String body, Map<String, List<String>> headers) {
        return new Response(status, headers, body.getBytes(java.nio.charset.StandardCharsets.UTF_8));
    }
}
