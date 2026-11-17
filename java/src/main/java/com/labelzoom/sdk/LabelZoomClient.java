package com.labelzoom.sdk;

import java.io.IOException;
import java.io.UncheckedIOException;
import java.io.UnsupportedEncodingException;
import java.net.URLEncoder;
import java.nio.charset.StandardCharsets;
import java.time.Duration;
import java.util.LinkedHashMap;
import java.util.Locale;
import java.util.Map;
import java.util.Random;
import java.util.concurrent.TimeUnit;
import java.util.function.Function;
import java.util.function.LongConsumer;

/**
 * Client for the LabelZoom API: label conversion and Cloud Print.
 *
 * <p>Thread-safe and intended to be long-lived. Create one per application, not one per request.
 *
 * <p><b>An API key is optional.</b> Constructed without one, the client uses the anonymous free
 * tier: watermarked output, first label only, a 1 MB request cap, and no multi-page, JSON-target,
 * or image-to-image conversion.
 *
 * <pre>{@code
 * try (LabelZoomClient client = LabelZoomClient.builder().build()) {
 *     ConversionResult result = client.convert()
 *             .fromZpl("^XA^FO20,20^A0N,28^FDHello^FS^XZ")
 *             .toPng()
 *                 .withDpi(300)
 *             .execute();
 *     result.save(Path.of("label.png"));
 * }
 * }</pre>
 */
public final class LabelZoomClient implements AutoCloseable {

    /** The production API host. */
    public static final String DEFAULT_BASE_URL = "https://api.labelzoom.com";

    /** The environment variable consulted when no credential is supplied. */
    public static final String API_KEY_ENVIRONMENT_VARIABLE = "LABELZOOM_API_KEY";

    private static final String REQUEST_ID_HEADER = "X-LZ-Request-Id";
    private static final int MAX_MESSAGE_LENGTH = 512;

    private final String baseUrl;
    private final String credential;
    private final int maxRetries;
    private final String userAgent;
    private final HttpTransport transport;
    private final boolean useJitter;
    private final LongConsumer sleeper;
    private final Random jitter = new Random();

    private LabelZoomClient(Builder builder) {
        this.baseUrl = builder.baseUrl.replaceAll("/+$", "");
        this.credential = resolveCredential(
                builder.apiKey, builder.apiKeySet, builder.environmentLookup);
        this.maxRetries = builder.maxRetries;
        this.useJitter = builder.useJitter;
        this.sleeper = builder.sleeper != null ? builder.sleeper : LabelZoomClient::sleepQuietly;

        String agent = "labelzoom-java-sdk/" + version() + " (java)";
        if (builder.userAgentSuffix != null && !Compat.isBlank(builder.userAgentSuffix)) {
            agent = agent + " " + builder.userAgentSuffix.trim();
        }
        this.userAgent = agent;

        this.transport = builder.transport != null
                ? builder.transport
                : JdkHttpTransport.createDefault(builder.timeout);
    }

    /** Creates a builder. Every option has a working default. */
    public static Builder builder() {
        return new Builder();
    }

    /** Whether a credential was resolved. False means anonymous free-tier requests. */
    public boolean isAuthenticated() {
        return credential != null;
    }

    /** Starts building a conversion. */
    public ConversionRequestBuilder convert() {
        return new ConversionRequestBuilder(this);
    }

    /**
     * Starts a print to a Cloud Print printer. Choose a document with a {@code from*} method, or a
     * published template with {@link PrintRequestBuilder#fromTemplate(String)}.
     *
     * <p>There is no target stage: the printer's own configuration picks the output language, and
     * LabelZoom converts the document to it. Printing needs an API key with the {@code print} scope.
     *
     * @param printerId the printer's id, from the Printers page or the printers list
     * @throws LabelZoomValidationException if {@code printerId} is null or blank
     */
    public PrintRequestBuilder print(String printerId) {
        return new PrintRequestBuilder(this, PrintWriters.requireId(printerId, "printerId"));
    }

    /** Print jobs: look one up by id to follow it to completion. */
    public PrintJobsClient jobs() {
        return new PrintJobsClient(this);
    }

    /**
     * Gets the account's Cloud Print limits and today's usage, so a caller can stop before it hits
     * a {@link LabelZoomPrintQuotaExceededException}.
     *
     * @throws LabelZoomException if the API returns a non-2xx response
     */
    public PrintQuota getPrintQuota() {
        HttpTransport.Request request = request(
                "GET", apiUri("/api/v3/print/quota"), PrintWriters.JSON_MEDIA_TYPE, null, null, null);
        return send(request, response -> PrintQuota.fromJson(PrintResponses.read(response)));
    }

    /** Releases resources. The JDK transport holds none, so this is a no-op by default. */
    @Override
    public void close() {
        // Present so try-with-resources reads naturally and a future pooled transport can hook in.
    }

    ConversionResult execute(
            SourceFormat source,
            TargetFormat target,
            byte[] body,
            String contentType,
            Map<String, Object> params,
            Map<String, String> rawQuery) {

        String uri = buildUri(source, target, params, rawQuery);

        // Accept must be */*. The server's `produces` list omits image/gif, image/bmp and
        // image/jpeg, so naming the target's exact media type yields a 406 from content
        // negotiation before the handler ever runs.
        HttpTransport.Request request = request("POST", uri, "*/*", body, contentType, null);
        return send(request, response -> new ConversionResult(
                response.body(),
                response.header("Content-Type"),
                response.statusCode(),
                response.header(REQUEST_ID_HEADER)));
    }

    /**
     * Builds a request with the identity headers every call shares.
     *
     * @param body the request body, or null for a request with none, which then has no
     *     {@code Content-Type}
     * @param idempotencyKey sent as {@code Idempotency-Key} when not null
     */
    HttpTransport.Request request(
            String method, String uri, String accept,
            byte[] body, String contentType, String idempotencyKey) {
        Map<String, String> headers = new LinkedHashMap<>();
        if (body != null) {
            headers.put("Content-Type", contentType);
        }
        headers.put("Accept", accept);
        headers.put("User-Agent", userAgent);
        if (credential != null) {
            headers.put("Authorization", "Bearer " + credential);
        }
        if (idempotencyKey != null) {
            headers.put("Idempotency-Key", idempotencyKey);
        }
        return new HttpTransport.Request(method, uri, headers, body);
    }

    /**
     * Sends a request under the retry policy (contract section F) and reads a success with
     * {@code onSuccess}.
     *
     * <p>The same request is sent on every attempt, so anything that must not change between
     * attempts, such as a print's {@code Idempotency-Key}, is fixed before the first one.
     */
    <T> T send(HttpTransport.Request request, Function<HttpTransport.Response, T> onSuccess) {
        int attempts = maxRetries + 1;

        for (int attempt = 1; ; attempt++) {
            HttpTransport.Response response;
            try {
                response = transport.send(request);
            } catch (IOException e) {
                if (attempt >= attempts) {
                    throw new UncheckedIOException("LabelZoom request failed", e);
                }
                delay(attempt, null);
                continue;
            } catch (InterruptedException e) {
                Thread.currentThread().interrupt();
                throw new IllegalStateException("LabelZoom request was interrupted", e);
            }

            if (response.statusCode() >= 200 && response.statusCode() < 300) {
                return onSuccess.apply(response);
            }

            LabelZoomException error = toException(response);

            // F5: the daily print limit's Retry-After is the UTC reset, hours away. Honoring it
            // would hang the call, so it surfaces at once.
            if (attempt >= attempts || !isRetryable(response.statusCode())
                    || error instanceof LabelZoomPrintQuotaExceededException) {
                throw error;
            }

            // Read Retry-After from the response rather than the error: only the rate-limit type
            // exposes it, and RFC 9110 allows the header on 503 as well.
            delay(attempt, retryAfterSeconds(response));
        }
    }

    /** The base URL joined with an API path and query. Ids are percent-encoded by the caller. */
    String apiUri(String pathAndQuery) {
        return baseUrl + pathAndQuery;
    }

    /** The {@code X-LZ-Request-Id} of a response, or null. */
    static String requestId(HttpTransport.Response response) {
        return response.header(REQUEST_ID_HEADER);
    }

    String buildUri(
            SourceFormat source,
            TargetFormat target,
            Map<String, Object> params,
            Map<String, String> rawQuery) {

        StringBuilder uri = new StringBuilder(baseUrl)
                .append("/api/v2/convert/")
                .append(source.wireToken())
                .append("/to/")
                .append(target.wireToken());

        StringBuilder query = new StringBuilder();
        if (!params.isEmpty()) {
            query.append("params=").append(encode(Json.write(params)));
        }
        for (Map.Entry<String, String> entry : rawQuery.entrySet()) {
            if (query.length() > 0) {
                query.append('&');
            }
            query.append(encode(entry.getKey())).append('=').append(encode(entry.getValue()));
        }

        // No options set means no query string at all, not an empty "?params={}".
        if (query.length() > 0) {
            uri.append('?').append(query);
        }
        return uri.toString();
    }

    /** Percent-encodes a query value or a path segment. */
    static String encode(String value) {
        // URLEncoder is form encoding, which differs from percent-encoding for space and a few
        // others. Correcting those keeps the JSON readable in server logs.
        // The String charset-name overload, not the Charset one: Android only has the latter
        // from API 33.
        String encoded;
        try {
            encoded = URLEncoder.encode(value, "UTF-8");
        } catch (UnsupportedEncodingException e) {
            throw new IllegalStateException("UTF-8 is a required charset", e);
        }
        return encoded
                .replace("+", "%20")
                .replace("*", "%2A")
                .replace("%7E", "~");
    }

    private LabelZoomException toException(HttpTransport.Response response) {
        String rawBody = response.body() == null
                ? ""
                : new String(response.body(), StandardCharsets.UTF_8);
        String message = extractMessage(rawBody);
        String requestId = response.header(REQUEST_ID_HEADER);
        int status = response.statusCode();
        // Contract E3: the body's top-level "code", for every error and not only print's.
        String code = JsonReader.topLevelString(rawBody, "code");

        return switch (status) {
            case 400 -> new LabelZoomBadRequestException(message, requestId, rawBody, code);
            case 401 -> new LabelZoomUnauthorizedException(message, requestId, rawBody, code);
            case 403 -> new LabelZoomForbiddenException(
                    message, requestId, rawBody, code,
                    message.toLowerCase(Locale.ROOT).contains("paid feature"));
            case 404 -> new LabelZoomNotFoundException(message, requestId, rawBody, code);
            case 413 -> new LabelZoomPayloadTooLargeException(message, requestId, rawBody, code);
            case 422 -> new LabelZoomUnprocessableException(message, requestId, rawBody, code);
            case 429 -> LabelZoomPrintQuotaExceededException.DAILY_JOB_LIMIT_CODE.equals(code)
                    ? new LabelZoomPrintQuotaExceededException(
                            message, requestId, rawBody, code, retryAfterSeconds(response))
                    : new LabelZoomRateLimitedException(
                            message, requestId, rawBody, code, retryAfterSeconds(response));
            default -> status >= 500
                    ? new LabelZoomServerException(status, message, requestId, rawBody, code)
                    : new LabelZoomException(status, message, requestId, rawBody, code);
        };
    }

    /**
     * Pulls the human-readable detail out of an error body.
     *
     * <p>Both error shapes in play put it on {@code message}: the gateway returns
     * {@code {"message": "..."}} and Spring returns
     * {@code {timestamp,status,error,message,path}}. Anything else — a rate-limit body keyed on
     * {@code error}, an HTML 502, a truncated fragment — falls through to the raw text.
     */
    static String extractMessage(String rawBody) {
        if (rawBody != null && !Compat.isBlank(rawBody)) {
            String parsed = JsonMessage.extract(rawBody);
            if (parsed != null && !Compat.isBlank(parsed)) {
                return truncate(parsed);
            }
            return truncate(rawBody.trim());
        }
        return "The LabelZoom API returned an error with no response body.";
    }

    private static String truncate(String value) {
        return value.length() <= MAX_MESSAGE_LENGTH ? value : value.substring(0, MAX_MESSAGE_LENGTH);
    }

    private static boolean isRetryable(int status) {
        return status == 429 || status >= 500;
    }

    private static Integer retryAfterSeconds(HttpTransport.Response response) {
        String header = response.header("Retry-After");
        if (header == null) {
            return null;
        }
        try {
            return Integer.valueOf(header.trim());
        } catch (NumberFormatException e) {
            // The HTTP-date form is legal but rare here; treating it as absent falls back to the
            // backoff curve, which is safe.
            return null;
        }
    }

    /** 1s, 2s, 4s with full jitter, overridden by a longer {@code Retry-After}. */
    private void delay(int attempt, Integer retryAfterSeconds) {
        long backoffMillis = 1000L << (attempt - 1);
        long delayMillis = useJitter
                ? (long) (backoffMillis * jitter.nextDouble())
                : backoffMillis;

        if (retryAfterSeconds != null) {
            long serverMillis = TimeUnit.SECONDS.toMillis(retryAfterSeconds);
            if (serverMillis > delayMillis) {
                delayMillis = serverMillis;
            }
        }
        sleeper.accept(delayMillis);
    }

    private static void sleepQuietly(long millis) {
        try {
            Thread.sleep(millis);
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
            throw new IllegalStateException("Interrupted while waiting to retry", e);
        }
    }

    private static String resolveCredential(
            String apiKey, boolean explicitlySet, java.util.function.UnaryOperator<String> environment) {
        if (!explicitlySet) {
            String fromEnvironment = environment.apply(API_KEY_ENVIRONMENT_VARIABLE);
            return fromEnvironment == null || fromEnvironment.isEmpty() ? null : fromEnvironment;
        }
        // An explicit null or empty forces anonymous and must not fall back to the environment.
        return apiKey == null || apiKey.isEmpty() ? null : apiKey;
    }

    private static String version() {
        String implementation = LabelZoomClient.class.getPackage().getImplementationVersion();
        return implementation != null ? implementation : "0.0.0-dev";
    }

    /** Builder for {@link LabelZoomClient}. */
    public static final class Builder {

        private String baseUrl = DEFAULT_BASE_URL;
        private String apiKey;
        private boolean apiKeySet;
        private int maxRetries = 2;
        private Duration timeout;
        private String userAgentSuffix;
        private HttpTransport transport;
        private boolean useJitter = true;
        private LongConsumer sleeper;
        // System.getenv is not mockable in Java, so the lookup itself is the seam. Package-private
        // rather than public: this exists for the conformance suite, not for callers.
        private java.util.function.UnaryOperator<String> environmentLookup = System::getenv;

        private Builder() {
        }

        /**
         * The bearer credential — an {@code lz_live_}/{@code lz_test_} key or a JWT.
         *
         * <p>Not calling this at all reads {@code LABELZOOM_API_KEY} from the environment. Calling
         * it with {@code null} or {@code ""} forces anonymous mode and suppresses that fallback.
         */
        public Builder apiKey(String apiKey) {
            this.apiKey = apiKey;
            this.apiKeySet = true;
            return this;
        }

        /** API base URL. A path prefix is preserved, so a reverse proxy works as expected. */
        public Builder baseUrl(String baseUrl) {
            if (baseUrl == null || Compat.isBlank(baseUrl)) {
                throw new IllegalArgumentException("Base URL cannot be null or blank.");
            }
            this.baseUrl = baseUrl;
            return this;
        }

        /** Retries after the initial attempt. Defaults to 2 (3 attempts total); 0 disables. */
        public Builder maxRetries(int maxRetries) {
            if (maxRetries < 0) {
                throw new IllegalArgumentException("maxRetries cannot be negative.");
            }
            this.maxRetries = maxRetries;
            return this;
        }

        /** Per-request timeout. */
        public Builder timeout(Duration timeout) {
            this.timeout = timeout;
            return this;
        }

        /** Appended to the SDK's own User-Agent, for identifying your application. */
        public Builder userAgentSuffix(String suffix) {
            this.userAgentSuffix = suffix;
            return this;
        }

        /** Replaces the HTTP layer. The seam the conformance suite and your own tests use. */
        public Builder transport(HttpTransport transport) {
            this.transport = transport;
            return this;
        }

        /** Whether to apply full jitter to retry backoff. Turn off for deterministic tests. */
        public Builder useJitter(boolean useJitter) {
            this.useJitter = useJitter;
            return this;
        }

        /** Replaces the delay between retries. Substitute a recording no-op in tests. */
        public Builder sleeper(LongConsumer sleeper) {
            this.sleeper = sleeper;
            return this;
        }

        /** Replaces the environment lookup used to resolve {@code LABELZOOM_API_KEY}. */
        Builder environmentLookup(java.util.function.UnaryOperator<String> environmentLookup) {
            this.environmentLookup = environmentLookup;
            return this;
        }

        /** Builds the client. */
        public LabelZoomClient build() {
            return new LabelZoomClient(this);
        }
    }
}
