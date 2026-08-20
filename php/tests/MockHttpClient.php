<?php

declare(strict_types=1);

namespace LabelZoom\Sdk\Tests;

use Nyholm\Psr7\Factory\Psr17Factory;
use Psr\Http\Client\ClientExceptionInterface;
use Psr\Http\Client\ClientInterface;
use Psr\Http\Message\RequestInterface;
use Psr\Http\Message\ResponseInterface;

/**
 * The conformance suite's HTTP stub.
 *
 * Never opens a socket: it records every outgoing request and replays a scripted queue of
 * responses, so the whole suite runs offline and passes identically on a fork pull request.
 */
final class MockHttpClient implements ClientInterface
{
    /** @var list<RequestInterface> */
    public array $requests = [];

    /** @var list<ResponseInterface|ClientExceptionInterface> */
    private array $queue = [];

    private readonly Psr17Factory $factory;

    public function __construct()
    {
        $this->factory = new Psr17Factory();
    }

    /**
     * @param array<string, string> $headers
     */
    public function enqueue(int $status, string $body, array $headers = []): void
    {
        $response = $this->factory->createResponse($status)
            ->withBody($this->factory->createStream($body));

        foreach ($headers as $name => $value) {
            $response = $response->withHeader($name, $value);
        }

        $this->queue[] = $response;
    }

    /** Queues a transport-level failure — connection refused, DNS, TLS, timeout. */
    public function enqueueTransportError(): void
    {
        $this->queue[] = new MockTransportException('simulated connect failure');
    }

    public function sendRequest(RequestInterface $request): ResponseInterface
    {
        $this->requests[] = $request;

        $next = array_shift($this->queue);
        if ($next === null) {
            throw new \LogicException(
                'The SDK made more requests than the fixture scripted responses for; '
                . 'attempt ' . count($this->requests) . ' had nothing queued.',
            );
        }
        if ($next instanceof ClientExceptionInterface) {
            throw $next;
        }

        return $next;
    }

    public function lastRequest(): RequestInterface
    {
        if ($this->requests === []) {
            throw new \LogicException('No request was sent.');
        }

        return $this->requests[count($this->requests) - 1];
    }
}
