<?php

declare(strict_types=1);

namespace LabelZoom\Sdk;

use LabelZoom\Sdk\Exception\BadRequestException;
use LabelZoom\Sdk\Exception\ForbiddenException;
use LabelZoom\Sdk\Exception\LabelZoomException;
use LabelZoom\Sdk\Exception\NotFoundException;
use LabelZoom\Sdk\Exception\PayloadTooLargeException;
use LabelZoom\Sdk\Exception\RateLimitedException;
use LabelZoom\Sdk\Exception\ServerErrorException;
use LabelZoom\Sdk\Exception\TransportException;
use LabelZoom\Sdk\Exception\UnauthorizedException;
use LabelZoom\Sdk\Http\CurlHttpClient;
use LabelZoom\Sdk\Internal\RealSleeper;
use Nyholm\Psr7\Factory\Psr17Factory;
use Psr\Http\Client\ClientExceptionInterface;
use Psr\Http\Client\ClientInterface;
use Psr\Http\Message\RequestFactoryInterface;
use Psr\Http\Message\ResponseInterface;
use Psr\Http\Message\StreamFactoryInterface;

/**
 * Client for the LabelZoom conversion API.
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
 *     ->withDpi(300)
 *     ->execute();
 *
 * $result->save('label.png');
 * ```
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
    public const VERSION = '1.0.0';

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
     * Performs the conversion. Called by {@see ConversionTargetBuilder::execute()}.
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
        $request = $this->requestFactory
            ->createRequest('POST', $this->buildUri($source, $target, $params, $rawQuery))
            ->withHeader('Content-Type', $contentType)
            // Accept must be */*. The server's `produces` list omits image/gif, image/bmp and
            // image/jpeg, so naming the target's exact media type yields a 406 from content
            // negotiation before the handler ever runs (rule B2).
            ->withHeader('Accept', '*/*')
            ->withHeader('User-Agent', $this->userAgent)
            ->withBody($this->streamFactory->createStream($body));

        // Absent, not empty: never `Bearer `, never `Bearer null` (rule B3).
        if ($this->credential !== null) {
            $request = $request->withHeader('Authorization', 'Bearer ' . $this->credential);
        }

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
                return new ConversionResult(
                    (string) $response->getBody(),
                    $this->headerOrNull($response, 'Content-Type'),
                    $status,
                    $this->headerOrNull($response, self::REQUEST_ID_HEADER),
                );
            }

            $error = $this->toException($response);

            if ($attempt >= $attempts || !self::isRetryable($status)) {
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
            $pairs[] = 'params=' . rawurlencode(self::encodeParams($params));
        }
        foreach ($rawQuery as $key => $value) {
            $pairs[] = rawurlencode($key) . '=' . rawurlencode($value);
        }

        // No options set means no query string at all, not an empty `?params={}` (rule C7).
        return $pairs === [] ? $uri : $uri . '?' . implode('&', $pairs);
    }

    /**
     * @param array<string, mixed> $params
     */
    private static function encodeParams(array $params): string
    {
        // PRESERVE_ZERO_FRACTION keeps `label.width: 4.0` a float rather than collapsing it to 4;
        // UNESCAPED_SLASHES and UNESCAPED_UNICODE keep the JSON legible in server logs.
        $json = json_encode(
            $params,
            JSON_THROW_ON_ERROR | JSON_PRESERVE_ZERO_FRACTION | JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE,
        );

        return $json;
    }

    private function toException(ResponseInterface $response): LabelZoomException
    {
        $rawBody = (string) $response->getBody();
        $message = self::extractMessage($rawBody);
        $requestId = $this->headerOrNull($response, self::REQUEST_ID_HEADER);
        $status = $response->getStatusCode();

        return match (true) {
            $status === 400 => new BadRequestException($status, $message, $requestId, $rawBody),
            $status === 401 => new UnauthorizedException($status, $message, $requestId, $rawBody),
            $status === 403 => new ForbiddenException(
                $status,
                $message,
                $requestId,
                $rawBody,
                preg_match('/paid feature/i', $message) === 1,
            ),
            $status === 404 => new NotFoundException($status, $message, $requestId, $rawBody),
            $status === 413 => new PayloadTooLargeException($status, $message, $requestId, $rawBody),
            $status === 429 => new RateLimitedException(
                $status,
                $message,
                $requestId,
                $rawBody,
                self::retryAfterSeconds($response),
            ),
            $status >= 500 => new ServerErrorException($status, $message, $requestId, $rawBody),
            // 406, 415 and friends: still a typed LabelZoomException, just not a named subclass.
            default => new LabelZoomException($status, $message, $requestId, $rawBody),
        };
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
