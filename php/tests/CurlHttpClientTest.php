<?php

declare(strict_types=1);

namespace LabelZoom\Sdk\Tests;

use LabelZoom\Sdk\Http\CurlHttpClient;
use LabelZoom\Sdk\Http\CurlTransportException;
use LabelZoom\Sdk\LabelZoomClient;
use LabelZoom\Sdk\SourceFormat;
use LabelZoom\Sdk\TargetFormat;
use Nyholm\Psr7\Factory\Psr17Factory;
use PHPUnit\Framework\TestCase;

/**
 * Exercises the bundled transport against a real socket.
 *
 * The conformance suite injects {@see MockHttpClient}, which means the client every user gets
 * by default was the one piece of the SDK nothing executed. That is how a `curl_close()` call
 * — a no-op since PHP 8.0, deprecated in 8.5 — shipped in 0.1.0 and emitted a notice on every
 * conversion while the suite stayed green.
 *
 * The server is PHP's own `-S`, bound to a loopback port, so this is still offline: no
 * outbound network, nothing to stub, and it passes on a fork pull request.
 */
final class CurlHttpClientTest extends TestCase
{
    private static ?string $baseUrl = null;

    /** @var resource|null */
    private static $process = null;

    /** @var array<int, resource> the descriptors proc_open filled — keys 1 and 2, not a list */
    private static array $pipes = [];

    public static function setUpBeforeClass(): void
    {
        // Port 0 asks the OS for a free one, which avoids the racy "pick a number and hope"
        // that makes this kind of test flaky when suites run in parallel.
        $socket = stream_socket_server('tcp://127.0.0.1:0', $errno, $errstr);
        if ($socket === false) {
            self::markTestSkipped("Could not reserve a loopback port: {$errstr}");
        }
        $name = (string) stream_socket_get_name($socket, false);
        $port = (int) substr($name, (int) strrpos($name, ':') + 1);
        fclose($socket);

        $command = [
            PHP_BINARY,
            '-S', "127.0.0.1:{$port}",
            '-t', __DIR__ . '/Fixtures',
            __DIR__ . '/Fixtures/echo-server.php',
        ];
        $ini = php_ini_loaded_file();
        if ($ini !== false) {
            array_splice($command, 1, 0, ['-c', $ini]);
        }

        $descriptors = [1 => ['pipe', 'w'], 2 => ['pipe', 'w']];
        $process = proc_open($command, $descriptors, self::$pipes);
        if (!is_resource($process)) {
            self::markTestSkipped('Could not start the PHP built-in server.');
        }
        self::$process = $process;
        self::$baseUrl = "http://127.0.0.1:{$port}";

        // Poll rather than sleep a fixed amount: startup is a few milliseconds locally and
        // noticeably slower on a loaded CI runner.
        for ($attempt = 0; $attempt < 100; ++$attempt) {
            $probe = @fsockopen('127.0.0.1', $port, $errno, $errstr, 0.1);
            if ($probe !== false) {
                fclose($probe);

                return;
            }
            usleep(50_000);
        }

        self::markTestSkipped('The PHP built-in server did not come up in time.');
    }

    public static function tearDownAfterClass(): void
    {
        foreach (self::$pipes as $pipe) {
            if (is_resource($pipe)) {
                fclose($pipe);
            }
        }
        self::$pipes = [];

        if (is_resource(self::$process)) {
            proc_terminate(self::$process);
            proc_close(self::$process);
        }
        self::$process = null;
        self::$baseUrl = null;
    }

    /**
     * The regression this whole file exists for.
     *
     * `failOnDeprecation` in phpunit.xml.dist turns a PHP deprecation into a failure, so on
     * PHP 8.5 a reintroduced `curl_close()` fails here rather than quietly adding a notice to
     * every conversion a user runs.
     */
    public function testASuccessfulRequestRaisesNoDeprecation(): void
    {
        $factory = new Psr17Factory();
        $request = $factory->createRequest('POST', self::$baseUrl . '/echo')
            ->withHeader('Content-Type', 'text/plain')
            ->withBody($factory->createStream('^XA^XZ'));

        $response = (new CurlHttpClient())->sendRequest($request);

        self::assertSame(200, $response->getStatusCode());
    }

    public function testItSendsMethodPathHeadersAndBody(): void
    {
        $client = new LabelZoomClient(
            apiKey: 'lz_test_abc',
            baseUrl: self::$baseUrl,
            maxRetries: 0,
        );

        // Goes through the real client so the URL, headers and body under test are the ones
        // the SDK actually builds, not ones the test hand-rolled.
        $result = $client->convert()
            ->from(SourceFormat::Zpl, '^XA^FO20,20^FDhi^FS^XZ')
            ->to(TargetFormat::Png)
            ->withDpi(300)
            ->execute();

        /** @var array{method: string, uri: string, headers: array<string, string>, body: string} $echo */
        $echo = json_decode($result->getText(), true, 512, JSON_THROW_ON_ERROR);

        self::assertSame('POST', $echo['method']);
        self::assertStringStartsWith('/api/v2/convert/zpl/to/png?params=', $echo['uri']);
        self::assertSame('text/plain', $echo['headers']['content-type']);
        self::assertSame('*/*', $echo['headers']['accept']);
        self::assertSame('Bearer lz_test_abc', $echo['headers']['authorization']);
        self::assertMatchesRegularExpression('#^labelzoom-php-sdk/#', $echo['headers']['user-agent']);
        self::assertSame('^XA^FO20,20^FDhi^FS^XZ', $echo['body']);
    }

    public function testItPreservesBinaryBodiesAndReadsResponseHeaders(): void
    {
        $factory = new Psr17Factory();
        $request = $factory->createRequest('POST', self::$baseUrl . '/binary')
            ->withBody($factory->createStream(''));

        $response = (new CurlHttpClient())->sendRequest($request);

        self::assertSame('image/png', $response->getHeaderLine('Content-Type'));
        // Byte-exact: the header callback splits on ':', and a body that went through any
        // string handling would lose the 0xFF 0xFE pair.
        self::assertSame("\x89PNG\r\n\x1a\n\x00\xFF\xFE\x01", (string) $response->getBody());
    }

    public function testItSurfacesAnErrorStatusWithoutThrowing(): void
    {
        $factory = new Psr17Factory();
        $request = $factory->createRequest('POST', self::$baseUrl . '/status?code=503')
            ->withBody($factory->createStream(''));

        // A PSR-18 client reports the response; deciding what a 503 means is the SDK's job.
        $response = (new CurlHttpClient())->sendRequest($request);

        self::assertSame(503, $response->getStatusCode());
        self::assertSame('req-from-server', $response->getHeaderLine('X-LZ-Request-Id'));
    }

    public function testAConnectionFailureBecomesAPsr18ClientException(): void
    {
        $factory = new Psr17Factory();
        // Port 1 on loopback: nothing listens, and the refusal is immediate.
        $request = $factory->createRequest('POST', 'http://127.0.0.1:1/nope')
            ->withBody($factory->createStream(''));

        $this->expectException(CurlTransportException::class);
        (new CurlHttpClient(timeoutSeconds: 2.0))->sendRequest($request);
    }
}
