<?php

declare(strict_types=1);

namespace LabelZoom\Sdk\Http;

use Nyholm\Psr7\Factory\Psr17Factory;
use Psr\Http\Client\ClientInterface;
use Psr\Http\Message\RequestInterface;
use Psr\Http\Message\ResponseFactoryInterface;
use Psr\Http\Message\ResponseInterface;
use Psr\Http\Message\StreamFactoryInterface;

/**
 * The PSR-18 client the SDK uses when you do not supply one.
 *
 * Deliberately minimal: it performs exactly one request and never retries, because the retry
 * policy belongs to {@see \LabelZoom\Sdk\LabelZoomClient} and two layers of it would multiply.
 *
 * Bring your own client — Guzzle, Symfony HttpClient, anything PSR-18 — by passing it to the
 * `LabelZoomClient` constructor. That is also the seam the conformance suite uses.
 */
final class CurlHttpClient implements ClientInterface
{
    private readonly ResponseFactoryInterface $responseFactory;
    private readonly StreamFactoryInterface $streamFactory;

    /**
     * @param float $timeoutSeconds        total time allowed for a request
     * @param float $connectTimeoutSeconds time allowed to establish the connection
     */
    public function __construct(
        private readonly float $timeoutSeconds = 30.0,
        private readonly float $connectTimeoutSeconds = 10.0,
        ?ResponseFactoryInterface $responseFactory = null,
        ?StreamFactoryInterface $streamFactory = null,
    ) {
        if (!extension_loaded('curl')) {
            throw new \RuntimeException(
                'The bundled CurlHttpClient needs ext-curl. Either enable it, or pass your own '
                . 'PSR-18 client to LabelZoomClient.',
            );
        }

        $factory = new Psr17Factory();
        $this->responseFactory = $responseFactory ?? $factory;
        $this->streamFactory = $streamFactory ?? $factory;
    }

    public function sendRequest(RequestInterface $request): ResponseInterface
    {
        $headers = [];
        foreach ($request->getHeaders() as $name => $values) {
            foreach ($values as $value) {
                $headers[] = "{$name}: {$value}";
            }
        }

        $url = (string) $request->getUri();
        $method = $request->getMethod();
        if ($url === '' || $method === '') {
            // cURL treats an empty URL or method as "use the default" rather than as an error,
            // so a malformed request would silently GET nothing instead of failing.
            throw new CurlTransportException('The request has no URL or no method.');
        }

        $handle = curl_init();
        if ($handle === false) {
            throw new CurlTransportException('Could not initialize a cURL handle.');
        }

        $responseHeaders = [];

        curl_setopt_array($handle, [
            CURLOPT_URL => $url,
            CURLOPT_CUSTOMREQUEST => $method,
            CURLOPT_HTTPHEADER => $headers,
            CURLOPT_POSTFIELDS => (string) $request->getBody(),
            CURLOPT_RETURNTRANSFER => true,
            CURLOPT_FOLLOWLOCATION => false,
            CURLOPT_TIMEOUT_MS => (int) round($this->timeoutSeconds * 1000),
            CURLOPT_CONNECTTIMEOUT_MS => (int) round($this->connectTimeoutSeconds * 1000),
            // Collected through a callback rather than CURLOPT_HEADER so the body does not have
            // to be split back out of one buffer -- which goes wrong the moment a proxy adds a
            // 100-Continue or a redirect preamble.
            CURLOPT_HEADERFUNCTION => static function ($_handle, string $line) use (&$responseHeaders): int {
                $length = strlen($line);
                $parts = explode(':', $line, 2);
                if (count($parts) === 2) {
                    $responseHeaders[trim($parts[0])][] = trim($parts[1]);
                }

                return $length;
            },
        ]);

        $body = curl_exec($handle);
        $errorNumber = curl_errno($handle);
        $errorMessage = curl_error($handle);
        $status = (int) curl_getinfo($handle, CURLINFO_RESPONSE_CODE);
        // No curl_close(): since PHP 8.0 the handle is an object freed by the garbage
        // collector, the call has done nothing at all, and 8.5 deprecates it — which means
        // calling it emits a notice on every single conversion.

        if ($errorNumber !== 0 || $body === false) {
            // A PSR-18 ClientExceptionInterface, which is what the retry policy treats as a
            // retryable transport failure.
            throw new CurlTransportException("cURL error {$errorNumber}: {$errorMessage}");
        }

        $response = $this->responseFactory->createResponse($status)
            ->withBody($this->streamFactory->createStream((string) $body));

        foreach ($responseHeaders as $name => $values) {
            $response = $response->withHeader($name, $values);
        }

        return $response;
    }
}
