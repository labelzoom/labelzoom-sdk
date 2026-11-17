<?php

declare(strict_types=1);

namespace LabelZoom\Sdk;

use LabelZoom\Sdk\Exception\BadRequestException;
use LabelZoom\Sdk\Exception\ForbiddenException;
use LabelZoom\Sdk\Exception\LabelZoomException;
use LabelZoom\Sdk\Exception\NotFoundException;
use LabelZoom\Sdk\Exception\PayloadTooLargeException;
use LabelZoom\Sdk\Exception\PrintQuotaExceededException;
use LabelZoom\Sdk\Exception\RateLimitedException;
use LabelZoom\Sdk\Exception\ServerErrorException;
use LabelZoom\Sdk\Exception\TransportException;
use LabelZoom\Sdk\Exception\UnauthorizedException;
use LabelZoom\Sdk\Exception\UnprocessableException;
use LabelZoom\Sdk\Http\CurlHttpClient;
use LabelZoom\Sdk\Internal\Json;
use LabelZoom\Sdk\Internal\PrintWriters;
use LabelZoom\Sdk\Internal\RealSleeper;
use Nyholm\Psr7\Factory\Psr17Factory;
use Psr\Http\Client\ClientExceptionInterface;
use Psr\Http\Client\ClientInterface;
use Psr\Http\Message\RequestFactoryInterface;
use Psr\Http\Message\RequestInterface;
use Psr\Http\Message\ResponseInterface;
use Psr\Http\Message\StreamFactoryInterface;

/**
 * Client for the LabelZoom conversion and Cloud Print APIs.
 *
 * Stateless once constructed and safe to keep for the life of the process — create one per
 * application, not one per request.
 *
 * **An API key is optional.** Constructed without one, the client uses the anonymous free tier:
 * watermarked output, first label only, a 1 MB request cap, and no multi-page, JSON-target or
 * image-to-image conversion.
 *
 * ```php
 * $client = new LabelZoomClient();
 *
 * $result = $client->convert()
 *     ->fromZpl('^XA^FO20,20^A0N,28^FDHello^FS^XZ')
 *     ->toPng()
 *         ->withDpi(300)
 *     ->execute();
 *
 * $result->save('label.png');
 * ```
 *
 * Printing goes through {@see self::print()} and needs an API key with the `print` scope.
 */
final class LabelZoomClient
{
    /** The production API host. */
    public const DEFAULT_BASE_URL = 'https://api.labelzoom.com';

    /** The environment variable consulted when no credential is supplied. */
    public const API_KEY_ENVIRONMENT_VARIABLE = 'LABELZOOM_API_KEY';

    /**
     * The SDK version, reported in `User-Agent`.
     *
     * Single-sourced here: the release workflow refuses to publish a tag that disagrees with it.
     */
    public const VERSION = '1.1.0';

    private const REQUEST_ID_HEADER = 'X-LZ-Request-Id';

    private const MAX_MESSAGE_LENGTH = 512;

    /**
     * Distinguishes "the caller said nothing about a key" from "the caller explicitly said none".
     *
     * PHP cannot tell an omitted named argument from one passed as null, and rule G2 turns on
     * exactly that difference: omitting it consults the environment, passing null or '' forces
     * anonymous mode and must not fall back.
     */
    private const API_KEY_UNSET = "\0__labelzoom_api_key_unset__\0";

    private readonly string $baseUrl;

    private readonly ?string $credential;

    private readonly int $maxRetries;

    private readonly string $userAgent;

    private readonly ClientInterface $httpClient;

    private readonly RequestFactoryInterface $requestFactory;

    private readonly StreamFactoryInterface $streamFactory;

    private readonly Sleeper $sleeper;

    private readonly bool $useJitter;

    /**
     * @param string|null                 $apiKey          bearer credential — an `lz_live_`/`lz_test_` key or a JWT.
     *                                                     Omit it entirely to read `LABELZOOM_API_KEY`; pass null or
     *                                                     '' to force anonymous mode and suppress that fallback.
     * @param string                      $baseUrl         API base URL. A path prefix is preserved, so a reverse
     *                                                     proxy works as expected.
     * @param int                         $maxRetries      retries *after* the initial attempt. 0 disables retry.
     * @param float                       $timeout         per-request timeout in seconds, for the bundled client
     * @param string|null                 $userAgentSuffix appended to the SDK's own User-Agent
     * @param ClientInterface|null        $httpClient      any PSR-18 client; the bundled cURL one by default
     * @param RequestFactoryInterface|null $requestFactory PSR-17 factory; nyholm/psr7 by default
     * @param StreamFactoryInterface|null $streamFactory   PSR-17 factory; nyholm/psr7 by default
     * @param (callable(string): (string|false))|null $environmentLookup replaces `getenv` when resolving the
     *                                                     credential. Present for the conformance suite —
     *                                                     `getenv` is process-global and cannot otherwise be
     *                                                     isolated between tests.
     * @param Sleeper|null                $sleeper         replaces the retry delay. Substitute a recorder in tests.
     * @param bool                        $useJitter       full jitter on retry backoff. Turn off for determinism.
     */
    public function __construct(
        ?string $apiKey = self::API_KEY_UNSET,
        string $baseUrl = self::DEFAULT_BASE_URL,
        int $maxRetries = 2,
        float $timeout = 30.0,
        ?string $userAgentSuffix = null,
        ?ClientInterface $httpClient = null,
        ?RequestFactoryInterface $requestFactory = null,
        ?StreamFactoryInterface $streamFactory = null,
        ?callable $environmentLookup = null,
        ?Sleeper $sleeper = null,
        bool $useJitter = true,
    ) {
        if (trim($baseUrl) === '') {
            throw new \InvalidArgumentException('Base URL cannot be empty.');
        }
        if ($maxRetries < 0) {
            throw new \InvalidArgumentException('maxRetries cannot be negative.');
        }

        $this->baseUrl = rtrim($baseUrl, '/');
        // Wrapped rather than passed as `getenv(...)`: the first-class callable resolves to
        // getenv's no-argument overload, which returns the whole environment as an array.
        $this->credential = self::resolveCredential(
            $apiKey,
            $environmentLookup ?? static fn (string $name): string|false => getenv($name),
        );
        $this->maxRetries = $maxRetries;
        $this->useJitter = $useJitter;
        $this->sleeper = $sleeper ?? new RealSleeper();

        $agent = 'labelzoom-php-sdk/' . self::VERSION . ' (php ' . PHP_VERSION . ')';
        if ($userAgentSuffix !== null && trim($userAgentSuffix) !== '') {
            $agent .= ' ' . trim($userAgentSuffix);
        }
        $this->userAgent = $agent;

        $factory = new Psr17Factory();
        $this->requestFactory = $requestFactory ?? $factory;
        $this->streamFactory = $streamFactory ?? $factory;
        $this->httpClient = $httpClient ?? new CurlHttpClient($timeout);
    }

    /** Whether a credential was resolved. False means anonymous free-tier requests. */
    public function isAuthenticated(): bool
    {
        return $this->credential !== null;
    }

    /** Starts building a conversion. */
    public function convert(): ConversionRequestBuilder
    {
        return new ConversionRequestBuilder($this);
    }

    /**
     * Starts a print to a Cloud Print printer. Choose a document with a `from*` method, or a
     * published template with {@see PrintRequestBuilder::fromTemplate()}.
     *
     * There is no target stage: the printer's own configuration picks the output language, and
     * LabelZoom converts the document to it. Printing needs an API key with the `print` scope.
     *
     * ```php
     * $job = $client->print($printerId)
     *     ->fromPdf($pdf)
     *         ->withPage(0)
     *     ->withCopies(2)
     *     ->send();
     * ```
     *
     * @param string $printerId the printer's id, from the Printers page or the printers list
     *
     * @throws Exception\ValidationException if the printer id is empty
     */
    public function print(string $printerId): PrintRequestBuilder
    {
        return new PrintRequestBuilder($this, PrintWriters::requireId($printerId, 'printerId'));
    }

    /** Print jobs: look one up by id to follow it to completion. */
    public function jobs(): PrintJobsClient
    {
        return new PrintJobsClient($this);
    }

    /**
     * Gets the account's Cloud Print limits and today's usage, so you can stop before a print
     * fails with a {@see PrintQuotaExceededException}.
     *
     * @throws LabelZoomException on any non-2xx response
     * @throws TransportException when no response was ever produced
     */
    public function getPrintQuota(): PrintQuota
    {
        return PrintQuota::fromJson($this->requestJson('GET', '/api/v3/print/quota'));
    }

    /**
     * Performs the conversion. Called by {@see TargetStage::execute()}.
     *
     * @param array<string, mixed>  $params   the `?params=` object, already validated
     * @param array<string, string> $rawQuery additional literal query parameters
     *
     * @throws LabelZoomException on any non-2xx response
     * @throws TransportException when no response was ever produced
     *
     * @internal
     */
    public function execute(
        SourceFormat $source,
        TargetFormat $target,
        string $body,
        string $contentType,
        array $params,
        array $rawQuery,
    ): ConversionResult {
        $request = $this->createRequest(
            'POST',
            $this->buildUri($source, $target, $params, $rawQuery),
            // Accept must be */*. The server's `produces` list omits image/gif, image/bmp and
            // image/jpeg, so naming the target's exact media type yields a 406 from content
            // negotiation before the handler ever runs (rule B2).
            '*/*',
            $body,
            $contentType,
        );

        $response = $this->send($request);

        return new ConversionResult(
            (string) $response->getBody(),
            $this->headerOrNull($response, 'Content-Type'),
            $response->getStatusCode(),
            $this->headerOrNull($response, self::REQUEST_ID_HEADER),
        );
    }

    /**
     * Sends a print API request and decodes its JSON response. Called by the print stages.
     *
     * Every print call accepts `application/json`, because every success response is JSON (rule
     * P1). Headers given here, such as the Idempotency-Key, are set once, so every retry of the
     * call sends the same ones (rule P4).
     *
     * @param string                $path    the path under the base URL, ids already percent-encoded
     * @param array<string, string> $query   literal query parameters, encoded here
     * @param array<string, string> $headers extra request headers
     *
     * @return array<mixed> the decoded response body
     *
     * @throws LabelZoomException on any non-2xx response
     * @throws TransportException when no response was ever produced
     *
     * @internal
     */
    public function requestJson(
        string $method,
        string $path,
        array $query = [],
        ?string $body = null,
        ?string $contentType = null,
        array $headers = [],
    ): array {
        $pairs = [];
        foreach ($query as $key => $value) {
            $pairs[] = rawurlencode($key) . '=' . rawurlencode($value);
        }
        $uri = $this->baseUrl . $path . ($pairs === [] ? '' : '?' . implode('&', $pairs));

        $request = $this->createRequest($method, $uri, PrintWriters::JSON_MEDIA_TYPE, $body, $contentType);
        foreach ($headers as $name => $value) {
            $request = $request->withHeader($name, $value);
        }

        $response = $this->send($request);
        $decoded = json_decode((string) $response->getBody(), true);
        if (!is_array($decoded)) {
            throw new \UnexpectedValueException(
                'The LabelZoom API returned a success response that is not a JSON object (request '
                . ($this->headerOrNull($response, self::REQUEST_ID_HEADER) ?? 'unknown') . ').',
            );
        }

        return $decoded;
    }

    /**
     * Builds a request with the headers every call shares: Accept, User-Agent and the credential.
     *
     * A null body sends none, and a null content type sends no `Content-Type`, as the print GETs
     * require (rule P1).
     */
    private function createRequest(
        string $method,
        string $uri,
        string $accept,
        ?string $body = null,
        ?string $contentType = null,
    ): RequestInterface {
        $request = $this->requestFactory
            ->createRequest($method, $uri)
            ->withHeader('Accept', $accept)
            ->withHeader('User-Agent', $this->userAgent);

        if ($contentType !== null) {
            $request = $request->withHeader('Content-Type', $contentType);
        }
        if ($body !== null) {
            $request = $request->withBody($this->streamFactory->createStream($body));
        }

        // Absent, not empty: never `Bearer `, never `Bearer null` (rule B3).
        if ($this->credential !== null) {
            $request = $request->withHeader('Authorization', 'Bearer ' . $this->credential);
        }

        return $request;
    }

    /**
     * Sends a request under the retry policy (contract section F) and returns the 2xx response.
     *
     * The same immutable PSR-7 request goes out on every attempt, so anything that must not change
     * between attempts, such as a print's Idempotency-Key, is already fixed on it.
     *
     * @throws LabelZoomException on any non-2xx response that is not retried, or the last one
     * @throws TransportException when no response was ever produced
     */
    private function send(RequestInterface $request): ResponseInterface
    {
        $attempts = $this->maxRetries + 1;

        for ($attempt = 1;; ++$attempt) {
            try {
                $response = $this->httpClient->sendRequest($request);
            } catch (ClientExceptionInterface $e) {
                if ($attempt >= $attempts) {
                    throw new TransportException('The LabelZoom request failed: ' . $e->getMessage(), $e);
                }
                $this->delay($attempt, null);

                continue;
            }

            $status = $response->getStatusCode();

            if ($status >= 200 && $status < 300) {
                return $response;
            }

            $error = $this->toException($response);

            // F5: the daily print limit's Retry-After is the 00:00 UTC reset, hours away, so it is
            // never retried. Every other 429 is.
            if (
                $attempt >= $attempts
                || !self::isRetryable($status)
                || $error instanceof PrintQuotaExceededException
            ) {
                throw $error;
            }

            // Read Retry-After from the response rather than off the typed error: only the
            // rate-limit type carries it, and RFC 9110 allows the header on 503 too.
            $this->delay($attempt, self::retryAfterSeconds($response));
        }
    }

    /**
     * @param array<string, mixed>  $params
     * @param array<string, string> $rawQuery
     *
     * @internal
     */
    public function buildUri(
        SourceFormat $source,
        TargetFormat $target,
        array $params,
        array $rawQuery,
    ): string {
        $uri = $this->baseUrl . '/api/v2/convert/' . $source->wireToken() . '/to/' . $target->wireToken();

        $pairs = [];
        if ($params !== []) {
            $pairs[] = 'params=' . rawurlencode(Json::encode($params));
        }
        foreach ($rawQuery as $key => $value) {
            $pairs[] = rawurlencode($key) . '=' . rawurlencode($value);
        }

        // No options set means no query string at all, not an empty `?params={}` (rule C7).
        return $pairs === [] ? $uri : $uri . '?' . implode('&', $pairs);
    }

    private function toException(ResponseInterface $response): LabelZoomException
    {
        $rawBody = (string) $response->getBody();
        $message = self::extractMessage($rawBody);
        $requestId = $this->headerOrNull($response, self::REQUEST_ID_HEADER);
        $status = $response->getStatusCode();
        $code = self::extractCode($rawBody);
        $dailyPrintLimit = $status === 429 && $code === PrintQuotaExceededException::DAILY_JOB_LIMIT_CODE;

        return match (true) {
            $status === 400 => new BadRequestException($status, $message, $requestId, $rawBody, $code),
            $status === 401 => new UnauthorizedException($status, $message, $requestId, $rawBody, $code),
            $status === 403 => new ForbiddenException(
                $status,
                $message,
                $requestId,
                $rawBody,
                preg_match('/paid feature/i', $message) === 1,
                $code,
            ),
            $status === 404 => new NotFoundException($status, $message, $requestId, $rawBody, $code),
            $status === 413 => new PayloadTooLargeException($status, $message, $requestId, $rawBody, $code),
            $status === 422 => new UnprocessableException($status, $message, $requestId, $rawBody, $code),
            $dailyPrintLimit => new PrintQuotaExceededException(
                $status,
                $message,
                $requestId,
                $rawBody,
                self::retryAfterSeconds($response),
                $code,
            ),
            $status === 429 => new RateLimitedException(
                $status,
                $message,
                $requestId,
                $rawBody,
                self::retryAfterSeconds($response),
                $code,
            ),
            $status >= 500 => new ServerErrorException($status, $message, $requestId, $rawBody, $code),
            // 406, 415 and friends: still a typed LabelZoomException, just not a named subclass.
            default => new LabelZoomException($status, $message, $requestId, $rawBody, $code),
        };
    }

    /**
     * The error body's top-level `code` string, or null when it has none (rule E3).
     *
     * Read from the full body rather than the truncated message, and never fails: a body that
     * is not JSON, or whose `code` is not a string, simply has no code.
     */
    private static function extractCode(string $rawBody): ?string
    {
        $decoded = json_decode(trim($rawBody), true);
        if (is_array($decoded) && isset($decoded['code']) && is_string($decoded['code'])) {
            return $decoded['code'];
        }

        return null;
    }

    /**
     * Pulls the human-readable detail out of an error body (rule E2).
     *
     * Both error shapes in play put it on `message`: the gateway returns `{"message": "..."}` and
     * Spring returns `{timestamp,status,error,message,path}`. Anything else — a rate-limit body
     * keyed on `error`, an HTML 502, a truncated JSON fragment — falls through to the raw text.
     */
    private static function extractMessage(string $rawBody): string
    {
        $trimmed = trim($rawBody);
        if ($trimmed === '') {
            return 'The LabelZoom API returned an error with no response body.';
        }

        $decoded = json_decode($trimmed, true);
        if (is_array($decoded) && isset($decoded['message']) && is_string($decoded['message'])) {
            $message = trim($decoded['message']);
            if ($message !== '') {
                return self::truncate($message);
            }
        }

        return self::truncate($trimmed);
    }

    private static function truncate(string $value): string
    {
        return strlen($value) <= self::MAX_MESSAGE_LENGTH
            ? $value
            : substr($value, 0, self::MAX_MESSAGE_LENGTH);
    }

    private static function isRetryable(int $status): bool
    {
        return $status === 429 || $status >= 500;
    }

    private static function retryAfterSeconds(ResponseInterface $response): ?int
    {
        $header = trim($response->getHeaderLine('Retry-After'));
        if ($header === '') {
            return null;
        }

        // The HTTP-date form is legal but vanishingly rare here; treating it as absent falls back
        // to the backoff curve, which is safe.
        return preg_match('/^\d+$/', $header) === 1 ? (int) $header : null;
    }

    /** 1s, 2s, 4s with full jitter, overridden by a longer `Retry-After` (rule F2). */
    private function delay(int $attempt, ?int $retryAfterSeconds): void
    {
        $backoff = (float) (1 << ($attempt - 1));
        $seconds = $this->useJitter ? $backoff * (mt_rand() / mt_getrandmax()) : $backoff;

        if ($retryAfterSeconds !== null && $retryAfterSeconds > $seconds) {
            $seconds = (float) $retryAfterSeconds;
        }

        $this->sleeper->sleep($seconds);
    }

    private function headerOrNull(ResponseInterface $response, string $name): ?string
    {
        // PSR-7 getHeaderLine is case-insensitive, which is what rule D2 needs: the gateway sets
        // X-LZ-Request-Id but CORS-exposes it as X-LZ-Request-ID.
        $value = $response->getHeaderLine($name);

        return $value === '' ? null : $value;
    }

    /**
     * @param (callable(string): (string|false)) $environmentLookup
     */
    private static function resolveCredential(?string $apiKey, callable $environmentLookup): ?string
    {
        if ($apiKey === self::API_KEY_UNSET) {
            $fromEnvironment = $environmentLookup(self::API_KEY_ENVIRONMENT_VARIABLE);

            return is_string($fromEnvironment) && $fromEnvironment !== '' ? $fromEnvironment : null;
        }

        // An explicit null or '' forces anonymous and must not fall back to the environment.
        return $apiKey === null || $apiKey === '' ? null : $apiKey;
    }
}
