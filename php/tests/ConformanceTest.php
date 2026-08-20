<?php

declare(strict_types=1);

namespace LabelZoom\Sdk\Tests;

use LabelZoom\Sdk\ColorMode;
use LabelZoom\Sdk\ConversionResult;
use LabelZoom\Sdk\ConversionTargetBuilder;
use LabelZoom\Sdk\Exception\BadRequestException;
use LabelZoom\Sdk\Exception\ForbiddenException;
use LabelZoom\Sdk\Exception\LabelZoomException;
use LabelZoom\Sdk\Exception\NotFoundException;
use LabelZoom\Sdk\Exception\PayloadTooLargeException;
use LabelZoom\Sdk\Exception\RateLimitedException;
use LabelZoom\Sdk\Exception\ServerErrorException;
use LabelZoom\Sdk\Exception\TransportException;
use LabelZoom\Sdk\Exception\UnauthorizedException;
use LabelZoom\Sdk\Exception\ValidationException;
use LabelZoom\Sdk\LabelZoomClient;
use LabelZoom\Sdk\PdfConversionMode;
use LabelZoom\Sdk\SourceFormat;
use LabelZoom\Sdk\TargetFormat;
use LabelZoom\Sdk\ZplImageCompression;
use PHPUnit\Framework\Attributes\DataProvider;
use PHPUnit\Framework\Attributes\Depends;
use PHPUnit\Framework\TestCase;
use Psr\Http\Message\RequestInterface;

/**
 * Runs the shared conformance fixtures against the PHP SDK.
 *
 * Entirely offline: the transport is {@see MockHttpClient}, so this passes identically on a fork
 * pull request with no secrets. The fixtures are the same ones the .NET, Node, Java and Python
 * suites run — see `docs/CONFORMANCE.md`.
 *
 * PHP declares **no skips**. The `typecheck/*` cases, which Python and Ruby skip for want of a
 * compile step, run here through PHPStan in {@see self::testTypecheckSnippetsAreRejected()}:
 * `SourceFormat` and `TargetFormat` are genuinely distinct enums, so `TargetFormat::Epl` does not
 * exist to be named and a `SourceFormat` is not accepted where a target belongs.
 */
final class ConformanceTest extends TestCase
{
    private const LANGUAGE = 'php';

    /**
     * The scripted 200 used by cases that assert on the request rather than the response.
     *
     * @var array<string, mixed>
     */
    private const OK_RESPONSE = [
        'status' => 200,
        'headers' => ['content-type' => 'text/plain'],
        'bodyText' => '^XA^XZ',
    ];

    /**
     * `expect.error.kind` → the PHP type it must be.
     *
     * @var array<string, class-string<LabelZoomException>>
     */
    private const ERROR_KINDS = [
        'BadRequest' => BadRequestException::class,
        'Unauthorized' => UnauthorizedException::class,
        'Forbidden' => ForbiddenException::class,
        'NotFound' => NotFoundException::class,
        'PayloadTooLarge' => PayloadTooLargeException::class,
        'RateLimited' => RateLimitedException::class,
        'ServerError' => ServerErrorException::class,
    ];

    /**
     * The `typecheck/*` fixtures rendered as PHP.
     *
     * The fixtures state their snippet in language-neutral pseudocode, so every statically
     * analysed SDK translates it; this map is PHP's translation and nothing more. The
     * pseudocode is quoted beside each one so a fixture edit that changes the *meaning*
     * is visible here rather than silently still passing.
     *
     * @var array<string, string>
     */
    private const TYPECHECK_SNIPPETS = [
        // client.convert().fromZpl(body).to(EPL)
        'typecheck/epl-is-not-a-target' => '$client->convert()->fromZpl($body)->to(TargetFormat::Epl);',
        // client.convert().fromZpl(body).to(TSPL)
        'typecheck/tspl-is-not-a-target' => '$client->convert()->fromZpl($body)->to(TargetFormat::Tspl);',
        // client.convert().fromZpl(body).to(DPL)
        'typecheck/dpl-is-not-a-target' => '$client->convert()->fromZpl($body)->to(TargetFormat::Dpl);',
        // client.convert().fromZpl(body).to(SourceFormat.PDF)
        'typecheck/source-format-not-accepted-as-target' => '$client->convert()->fromZpl($body)->to(SourceFormat::Pdf);',
    ];

    /**
     * Every case id this run actually executed. The completeness assertion reads it.
     *
     * @var array<string, true>
     */
    private static array $executed = [];

    // ------------------------------------------------------------------ fixtures

    private static function conformanceRoot(): string
    {
        for ($directory = __DIR__; $directory !== dirname($directory); $directory = dirname($directory)) {
            if (is_dir($directory . '/conformance/cases')) {
                return $directory . '/conformance';
            }
        }

        throw new \RuntimeException('Could not locate the conformance/ directory.');
    }

    /**
     * @return array<string, mixed>
     */
    private static function readJson(string $path): array
    {
        $raw = file_get_contents($path);
        if ($raw === false) {
            throw new \RuntimeException("Could not read {$path}");
        }

        /** @var array<string, mixed> $decoded */
        $decoded = json_decode($raw, true, 512, JSON_THROW_ON_ERROR);

        return $decoded;
    }

    /**
     * @return array<string, mixed>
     */
    private static function spec(): array
    {
        return self::readJson(self::conformanceRoot() . '/spec.json');
    }

    /**
     * Declared skips, as id → reason. PHP declares none; the file is still read so that
     * adding one has to go through the completeness assertion like every other language.
     *
     * @return array<string, string>
     */
    private static function skips(): array
    {
        $path = self::conformanceRoot() . '/skips/' . self::LANGUAGE . '.json';
        if (!is_file($path)) {
            return [];
        }

        $skips = [];
        /** @var list<array{id: string, reason: string}> $declared */
        $declared = self::readJson($path)['skips'];
        foreach ($declared as $skip) {
            $skips[$skip['id']] = $skip['reason'];
        }

        return $skips;
    }

    /**
     * @return list<string>
     */
    private static function expectedCaseIds(): array
    {
        $skips = self::skips();
        /** @var list<string> $cases */
        $cases = self::spec()['cases'];

        return array_values(array_filter($cases, static fn (string $id): bool => !isset($skips[$id])));
    }

    /**
     * The request/response/retry/validation cases, one PHPUnit case each.
     *
     * `typecheck/*` is excluded here and asserted by its own test — it needs a static analyser,
     * not a client call.
     *
     * @return iterable<string, array{string}>
     */
    public static function cases(): iterable
    {
        foreach (self::expectedCaseIds() as $id) {
            if (!str_starts_with($id, 'typecheck/')) {
                yield $id => [$id];
            }
        }
    }

    // ------------------------------------------------------------------ execution

    /**
     * Builds the client for one case and runs it, returning everything the assertions need.
     *
     * @param array<string, mixed>       $given
     * @param list<array<string, mixed>> $script
     *
     * @return array{ConversionResult|null, \Throwable|null, MockHttpClient, list<float>}
     */
    private function runCase(array $given, array $script, ?int $defaultMaxRetries = null): array
    {
        $http = new MockHttpClient();
        foreach ($script as $scripted) {
            if (isset($scripted['transportError'])) {
                $http->enqueueTransportError();

                continue;
            }
            /** @var array<string, string> $headers */
            $headers = $scripted['headers'] ?? [];
            $http->enqueue((int) $scripted['status'], self::scriptedBody($scripted), $headers);
        }

        $sleeper = new RecordingSleeper();

        /** @var array<string, mixed>|null $clientSpec */
        $clientSpec = $given['client'] ?? null;

        $arguments = [
            'httpClient' => $http,
            'sleeper' => $sleeper,
            // The fixtures assert exact sleep durations, so the backoff must be deterministic.
            'useJitter' => false,
            // Never `getenv`. A developer's real LABELZOOM_API_KEY would otherwise decide
            // `auth-absent`, making it pass locally and fail in CI — or, worse, the reverse.
            'environmentLookup' => static function (string $name) use ($given): string|false {
                /** @var array<string, string> $env */
                $env = $given['env'] ?? [];

                return $env[$name] ?? false;
            },
        ];

        if ($clientSpec === null) {
            // A case with no `client` block is not exercising credential resolution. Force
            // anonymous rather than leaving it to the (stubbed, empty) environment.
            $arguments['apiKey'] = null;
        } elseif (array_key_exists('apiKey', $clientSpec)) {
            // Present-and-null means "no credential". Omitting the key entirely — the `{}`
            // client block — means "consult the environment", which is rule G2's distinction
            // and the reason the constructor has an UNSET sentinel at all.
            /** @var string|null $apiKey */
            $apiKey = $clientSpec['apiKey'];
            $arguments['apiKey'] = $apiKey;
        }

        if (isset($clientSpec['baseUrl'])) {
            $arguments['baseUrl'] = (string) $clientSpec['baseUrl'];
        }

        $maxRetries = $given['maxRetries'] ?? $defaultMaxRetries;
        if ($maxRetries !== null) {
            $arguments['maxRetries'] = (int) $maxRetries;
        }

        $client = new LabelZoomClient(...$arguments);

        $result = null;
        $error = null;

        try {
            $result = $this->build($client, $given)->execute();
        } catch (LabelZoomException | ValidationException | TransportException $caught) {
            $error = $caught;
        }

        return [$result, $error, $http, $sleeper->sleeps];
    }

    /**
     * Translates the fixture's wire-shaped call into the SDK's fluent chain.
     *
     * This translation layer is the only per-language code in a runner. Writing it is what
     * proves PHP has no divergence to declare in API_CONTRACT.md §9 — the chain takes the
     * canonical shape directly.
     *
     * @param array<string, mixed> $given
     */
    private function build(LabelZoomClient $client, array $given): ConversionTargetBuilder
    {
        $source = SourceFormat::from((string) $given['source']);
        $target = TargetFormat::from((string) $given['target']);
        $body = (string) $given['bodyText'];

        $request = $client->convert();
        $sourceBuilder = ($given['sourceEncoding'] ?? null) === 'base64text'
            ? $request->fromBase64Text($source, $body)
            : $request->from($source, $body);

        $builder = $sourceBuilder->to($target);

        /** @var array<string, mixed> $options */
        $options = $given['options'] ?? [];
        foreach ($options as $key => $value) {
            $builder = match ($key) {
                'dpi' => $builder->withDpi((int) $value),
                'rotation' => $builder->withRotation((int) $value),
                'scaling' => $builder->withScaling((float) $value),
                'darkness' => $builder->withDarkness((int) $value),
                'watermark' => $builder->withWatermark((bool) $value),
                'dialect' => $builder->withDialect((string) $value),
                'colorMode' => $builder->withColorMode(ColorMode::from((string) $value)),
                'position' => $builder->withPosition((int) $value['x'], (int) $value['y']),
                'label' => $builder->withLabelSize((float) $value['width'], (float) $value['height']),
                'data' => $builder->withData($value),
                'pdf' => self::applyPdf($builder, $value),
                'zpl' => self::applyZpl($builder, $value),
                // Silently ignoring an unmapped option would let a new fixture pass without
                // ever exercising what it was added to pin.
                default => throw new \LogicException(
                    "Fixture sets option '{$key}', which the PHP runner does not map. "
                    . 'Add it to ConformanceTest::build() rather than skipping the case.',
                ),
            };
        }

        return $builder;
    }

    /**
     * @param array<string, mixed> $pdf
     */
    private static function applyPdf(ConversionTargetBuilder $builder, array $pdf): ConversionTargetBuilder
    {
        if (isset($pdf['conversionMode'])) {
            $builder = $builder->withPdfConversionMode(PdfConversionMode::from((string) $pdf['conversionMode']));
        }
        if (isset($pdf['pageNumber'])) {
            $builder = $builder->withPdfPage((int) $pdf['pageNumber']);
        }

        return $builder;
    }

    /**
     * @param array<string, mixed> $zpl
     */
    private static function applyZpl(ConversionTargetBuilder $builder, array $zpl): ConversionTargetBuilder
    {
        if (isset($zpl['commandsToIgnore'])) {
            /** @var list<string> $commands */
            $commands = $zpl['commandsToIgnore'];
            $builder = $builder->withZplCommandsToIgnore($commands);
        }
        if (isset($zpl['imageCompression'])) {
            $builder = $builder->withZplImageCompression(
                ZplImageCompression::from((string) $zpl['imageCompression']),
            );
        }

        return $builder;
    }

    /**
     * @param array<string, mixed> $scripted
     */
    private static function scriptedBody(array $scripted): string
    {
        if (isset($scripted['bodyBase64'])) {
            $decoded = base64_decode((string) $scripted['bodyBase64'], true);
            if ($decoded === false) {
                throw new \RuntimeException('Fixture bodyBase64 is not valid base64.');
            }

            return $decoded;
        }

        if (isset($scripted['bodyTextRepeat'])) {
            [$fill, $count] = $scripted['bodyTextRepeat'];

            return str_repeat((string) $fill, (int) $count);
        }

        return (string) ($scripted['bodyText'] ?? '');
    }

    // ------------------------------------------------------------------ the suite

    #[DataProvider('cases')]
    public function testConformance(string $caseId): void
    {
        $fixture = self::readJson(self::conformanceRoot() . "/cases/{$caseId}.json");
        /** @var array<string, mixed> $given */
        $given = $fixture['given'];
        /** @var array<string, mixed> $expect */
        $expect = $fixture['expect'];

        match (explode('/', $caseId)[0]) {
            'request' => $this->assertRequestCase($given, $expect),
            'response' => $this->assertResponseCase($given, $expect),
            'retry' => $this->assertRetryCase($given, $expect),
            'validation' => $this->assertValidationCase($given, $expect),
            default => throw new \LogicException("Unknown case kind for '{$caseId}'."),
        };

        self::$executed[$caseId] = true;
    }

    /**
     * @param array<string, mixed> $given
     * @param array<string, mixed> $expect
     */
    private function assertRequestCase(array $given, array $expect): void
    {
        [, $error, $http] = $this->runCase($given, [self::OK_RESPONSE]);
        self::assertNull($error, 'unexpected error: ' . ($error?->getMessage() ?? ''));

        $request = $http->lastRequest();
        $uri = $request->getUri();

        if (isset($expect['method'])) {
            self::assertSame($expect['method'], $request->getMethod());
        }
        if (isset($expect['url'])) {
            $authority = $uri->getHost() . ($uri->getPort() === null ? '' : ':' . $uri->getPort());
            self::assertSame($expect['url'], $uri->getScheme() . '://' . $authority . $uri->getPath());
        }
        if (isset($expect['path'])) {
            self::assertSame($expect['path'], $uri->getPath());
        }

        $this->assertHeaders($request, $expect);

        $query = self::parseQuery($uri->getQuery());

        /** @var array<string, mixed> $queryJson */
        $queryJson = $expect['queryJson'] ?? [];
        foreach ($queryJson as $name => $expectedJson) {
            self::assertArrayHasKey($name, $query, "query parameter {$name}");
            // Structural, never textual: JSON key order differs per language and
            // percent-encoding differs per standard library, so comparing the encoded
            // string would be flake by construction.
            self::assertEquals(
                self::canonicalize($expectedJson),
                self::canonicalize(json_decode($query[$name], true, 512, JSON_THROW_ON_ERROR)),
                "query parameter {$name}",
            );
        }

        /** @var list<string> $queryAbsent */
        $queryAbsent = $expect['queryAbsent'] ?? [];
        foreach ($queryAbsent as $name) {
            self::assertArrayNotHasKey($name, $query, "query parameter {$name} must be absent");
        }

        /** @var array<string, list<string>> $absentKeys */
        $absentKeys = $expect['queryJsonAbsentKeys'] ?? [];
        foreach ($absentKeys as $name => $keys) {
            /** @var array<string, mixed> $actual */
            $actual = json_decode($query[$name], true, 512, JSON_THROW_ON_ERROR);
            foreach ($keys as $key) {
                self::assertArrayNotHasKey($key, $actual, "{$name}.{$key} must not be serialized");
            }
        }

        if (isset($expect['bodyText'])) {
            self::assertSame($expect['bodyText'], (string) $request->getBody());
        }
    }

    /**
     * `expect.headers` is a subset assertion — extra headers the HTTP stack adds are fine —
     * while `headersAbsent` is exact. PSR-7 header lookups are case-insensitive, which is the
     * semantics the fixtures assume.
     *
     * @param array<string, mixed> $expect
     */
    private function assertHeaders(RequestInterface $request, array $expect): void
    {
        /** @var array<string, string> $headers */
        $headers = $expect['headers'] ?? [];
        foreach ($headers as $name => $value) {
            self::assertSame($value, $request->getHeaderLine($name), "header {$name}");
        }

        /** @var list<string> $absent */
        $absent = $expect['headersAbsent'] ?? [];
        foreach ($absent as $name) {
            self::assertFalse($request->hasHeader($name), "header {$name} must be absent");
        }

        /** @var array<string, string> $match */
        $match = $expect['headersMatch'] ?? [];
        foreach ($match as $name => $pattern) {
            self::assertMatchesRegularExpression(
                '/' . str_replace('/', '\/', $pattern) . '/',
                $request->getHeaderLine($name),
                "header {$name}",
            );
        }

        /** @var array<string, string> $notMatch */
        $notMatch = $expect['headersNotMatch'] ?? [];
        foreach ($notMatch as $name => $pattern) {
            self::assertDoesNotMatchRegularExpression(
                '/' . str_replace('/', '\/', $pattern) . '/',
                $request->getHeaderLine($name),
                "header {$name}",
            );
        }
    }

    /**
     * @param array<string, mixed> $given
     * @param array<string, mixed> $expect
     */
    private function assertResponseCase(array $given, array $expect): void
    {
        // Response cases queue exactly one response and assert how it maps. Retry is the
        // subject of retry/*, and leaving it on here would consume responses that the 429
        // and 5xx cases never scripted.
        $call = ['source' => 'zpl', 'target' => 'zpl', 'bodyText' => '^XA^XZ'];
        [$result, $error] = $this->runCase($call, [$given], defaultMaxRetries: 0);

        if (isset($expect['error'])) {
            /** @var array<string, mixed> $expectedError */
            $expectedError = $expect['error'];
            $this->assertError($expectedError, $error);

            return;
        }

        self::assertNull($error, 'unexpected error: ' . ($error?->getMessage() ?? ''));
        self::assertInstanceOf(ConversionResult::class, $result);

        /** @var array<string, mixed> $expected */
        $expected = $expect['result'];
        if (isset($expected['status'])) {
            self::assertSame($expected['status'], $result->getStatus());
        }
        if (array_key_exists('contentType', $expected)) {
            self::assertSame($expected['contentType'], $result->getContentType());
        }
        if (isset($expected['text'])) {
            self::assertSame($expected['text'], $result->getText());
        }
        if (isset($expected['bytesBase64'])) {
            self::assertSame($expected['bytesBase64'], base64_encode($result->getBytes()));
        }
        if (array_key_exists('requestId', $expected)) {
            self::assertSame($expected['requestId'], $result->getRequestId());
        }
    }

    /**
     * @param array<string, mixed> $given
     * @param array<string, mixed> $expect
     */
    private function assertRetryCase(array $given, array $expect): void
    {
        $call = $given + ['source' => 'zpl', 'target' => 'zpl', 'bodyText' => '^XA^XZ'];
        /** @var list<array<string, mixed>> $responses */
        $responses = $given['responses'];
        [$result, $error, $http, $sleeps] = $this->runCase($call, $responses);

        if (isset($expect['error'])) {
            /** @var array<string, mixed> $expectedError */
            $expectedError = $expect['error'];
            $this->assertError($expectedError, $error);
        } else {
            self::assertNull($error, 'unexpected error: ' . ($error?->getMessage() ?? ''));
            if (isset($expect['result']['text'])) {
                self::assertInstanceOf(ConversionResult::class, $result);
                self::assertSame($expect['result']['text'], $result->getText());
            }
        }

        self::assertCount($expect['attempts'], $http->requests, 'attempts');
        self::assertEquals($expect['sleepsSeconds'], $sleeps, 'recorded sleeps');
    }

    /**
     * @param array<string, mixed> $given
     * @param array<string, mixed> $expect
     */
    private function assertValidationCase(array $given, array $expect): void
    {
        [, $error, $http] = $this->runCase($given, [self::OK_RESPONSE]);

        self::assertInstanceOf(
            ValidationException::class,
            $error,
            'expected a local validation error, got ' . get_debug_type($error),
        );
        self::assertSame($expect['validationError']['parameter'], $error->getParameter());
        // Local validation must never reach the network.
        self::assertCount($expect['requestsSent'], $http->requests, 'requests sent');
    }

    /**
     * @param array<string, mixed> $expected
     */
    private function assertError(array $expected, ?\Throwable $actual): void
    {
        self::assertInstanceOf(
            LabelZoomException::class,
            $actual,
            'expected a LabelZoomException, got ' . get_debug_type($actual),
        );

        if (isset($expected['kind'])) {
            self::assertInstanceOf(self::ERROR_KINDS[$expected['kind']], $actual);
        }
        if (isset($expected['status'])) {
            self::assertSame($expected['status'], $actual->getStatus());
        }
        if (isset($expected['message'])) {
            self::assertSame($expected['message'], $actual->getMessage());
        }
        if (($expected['messageNonEmpty'] ?? false) === true) {
            self::assertNotSame('', trim($actual->getMessage()));
        }
        if (isset($expected['messageMaxLength'])) {
            self::assertLessThanOrEqual($expected['messageMaxLength'], strlen($actual->getMessage()));
        }
        if (isset($expected['rawBodyLength'])) {
            self::assertSame($expected['rawBodyLength'], strlen($actual->getRawBody()));
        }
        if (($expected['rawBodyPresent'] ?? false) === true) {
            self::assertNotSame('', $actual->getRawBody());
        }
        if (array_key_exists('requestId', $expected)) {
            self::assertSame($expected['requestId'], $actual->getRequestId());
        }
        if (isset($expected['isPaidFeature'])) {
            self::assertInstanceOf(ForbiddenException::class, $actual);
            self::assertSame($expected['isPaidFeature'], $actual->isPaidFeature());
        }
        if (array_key_exists('retryAfterSeconds', $expected)) {
            self::assertInstanceOf(RateLimitedException::class, $actual);
            self::assertSame($expected['retryAfterSeconds'], $actual->getRetryAfterSeconds());
        }
    }

    /**
     * The `typecheck/*` cases, run through PHPStan rather than through the client.
     *
     * PHP has no compile step, but it does have a static analyser that the SDK already ships a
     * config for — so these cases are executed rather than skipped. The snippets are analysed at
     * the level from `phpstan.neon.dist`, read at runtime rather than hardcoded: the claim being
     * made is that the SDK's own configured analysis rejects these, so if that level is ever
     * lowered, this test has to fail rather than quietly keep asserting an obsolete standard.
     */
    public function testTypecheckSnippetsAreRejected(): void
    {
        $expected = array_values(array_filter(
            self::expectedCaseIds(),
            static fn (string $id): bool => str_starts_with($id, 'typecheck/'),
        ));

        // Exactly, in both directions: a snippet with no fixture is dead weight, and a fixture
        // with no snippet would otherwise be quietly dropped and then reported as covered.
        $mapped = array_keys(self::TYPECHECK_SNIPPETS);
        sort($expected);
        sort($mapped);
        self::assertSame(
            $expected,
            $mapped,
            'TYPECHECK_SNIPPETS must cover exactly the typecheck cases PHP does not skip.',
        );

        if ($expected === []) {
            return;
        }

        $directory = sys_get_temp_dir() . '/labelzoom-php-typecheck-' . getmypid();
        self::removeDirectory($directory);
        mkdir($directory, 0o777, true);

        $fileForCase = [];
        foreach ($expected as $index => $caseId) {
            $file = $directory . '/snippet' . $index . '.php';
            file_put_contents($file, self::snippetProgram(self::TYPECHECK_SNIPPETS[$caseId]));
            $fileForCase[$caseId] = $file;
        }

        $report = self::runPhpstan($directory);
        self::removeDirectory($directory);

        foreach ($fileForCase as $caseId => $file) {
            $errors = $report['files'][$file]['errors'] ?? 0;
            self::assertGreaterThan(
                0,
                $errors,
                "PHPStan accepted the snippet for {$caseId}; it must be a static error.\n"
                . self::TYPECHECK_SNIPPETS[$caseId],
            );
            self::$executed[$caseId] = true;
        }
    }

    /**
     * The `level:` from `phpstan.neon.dist` — the one the SDK is really analysed at.
     *
     * Note the type distinction only bites from level 5 up, where PHPStan starts checking
     * argument types: below that, passing a `SourceFormat` where a `TargetFormat` belongs is
     * accepted. That is a property of PHPStan, not a weakness in the enums, and it is exactly
     * why this reads the configured level instead of picking a conservative one.
     */
    private static function phpstanLevel(): string
    {
        $config = (string) file_get_contents(dirname(__DIR__) . '/phpstan.neon.dist');
        self::assertSame(1, preg_match('/^\s*level:\s*(\S+)/m', $config, $matches), 'phpstan.neon.dist declares no level.');

        return $matches[1];
    }

    private static function snippetProgram(string $snippet): string
    {
        return <<<PHP
            <?php

            declare(strict_types=1);

            use LabelZoom\\Sdk\\LabelZoomClient;
            use LabelZoom\\Sdk\\SourceFormat;
            use LabelZoom\\Sdk\\TargetFormat;

            \$client = new LabelZoomClient(null);
            \$body = '^XA^XZ';

            {$snippet}

            PHP;
    }

    /**
     * @return array{files?: array<string, array{errors?: int}>}
     */
    private static function runPhpstan(string $directory): array
    {
        $root = dirname(__DIR__);
        $phpstan = $root . '/vendor/bin/phpstan';
        self::assertFileExists($phpstan, 'PHPStan is a dev dependency; run `composer install`.');

        // Re-use this process's own php.ini so the child has the same extensions. Without it a
        // PHP invoked with an explicit `-c` (which is how a no-root local install runs) would
        // start the child with no extensions at all and fail for the wrong reason.
        $ini = php_ini_loaded_file();

        $command = array_merge(
            [PHP_BINARY],
            $ini === false ? [] : ['-c', $ini],
            [
                $phpstan,
                'analyse',
                '--level=' . self::phpstanLevel(),
                '--no-progress',
                '--error-format=json',
                '--autoload-file=' . $root . '/vendor/autoload.php',
                $directory,
            ],
        );

        $descriptors = [1 => ['pipe', 'w'], 2 => ['pipe', 'w']];
        $process = proc_open($command, $descriptors, $pipes, $root);
        self::assertIsResource($process, 'Could not start PHPStan.');

        $stdout = stream_get_contents($pipes[1]);
        $stderr = stream_get_contents($pipes[2]);
        fclose($pipes[1]);
        fclose($pipes[2]);
        proc_close($process);

        /** @var array{files?: array<string, array{errors?: int}>}|null $report */
        $report = json_decode((string) $stdout, true);
        self::assertIsArray(
            $report,
            "PHPStan produced no JSON report.\nstdout: {$stdout}\nstderr: {$stderr}",
        );

        return $report;
    }

    private static function removeDirectory(string $directory): void
    {
        if (!is_dir($directory)) {
            return;
        }
        foreach (glob($directory . '/*') ?: [] as $file) {
            unlink($file);
        }
        rmdir($directory);
    }

    /**
     * The whole anti-drift mechanism.
     *
     * A suite that quietly runs a subset of the fixtures reports success exactly like one that
     * runs all of them, so coverage is asserted rather than assumed. `#[Depends]` is what makes
     * it reliable: it forces this to run after the typecheck test, and PHPUnit's default
     * execution order runs the data-provided cases before either.
     */
    #[Depends('testTypecheckSnippetsAreRejected')]
    public function testCoversEveryDeclaredCase(): void
    {
        /** @var list<string> $allCaseIds */
        $allCaseIds = self::spec()['cases'];

        foreach (self::skips() as $caseId => $reason) {
            self::assertContains(
                $caseId,
                $allCaseIds,
                'skips/' . self::LANGUAGE . ".json declares unknown case '{$caseId}'",
            );
            self::assertNotSame('', trim($reason), "skip '{$caseId}' has no reason");
        }

        $executed = array_keys(self::$executed);
        sort($executed);
        $expected = self::expectedCaseIds();
        sort($expected);

        self::assertSame($expected, $executed);
    }

    // ------------------------------------------------------------------ helpers

    /**
     * Splits a query string without `parse_str`, which mangles keys containing `.` or `[`.
     *
     * @return array<string, string>
     */
    private static function parseQuery(string $query): array
    {
        if ($query === '') {
            return [];
        }

        $parsed = [];
        foreach (explode('&', $query) as $pair) {
            if ($pair === '') {
                continue;
            }
            [$key, $value] = array_pad(explode('=', $pair, 2), 2, '');
            $parsed[rawurldecode($key)] = rawurldecode($value);
        }

        return $parsed;
    }

    /**
     * Puts decoded JSON into a comparable shape: keys sorted, numbers widened to float.
     *
     * Key order is not meaningful in JSON and differs per language, and `4` and `4.0` are the
     * same number however a given encoder chose to print it.
     */
    private static function canonicalize(mixed $value): mixed
    {
        if (is_array($value)) {
            $canonical = array_map(self::canonicalize(...), $value);
            if (!array_is_list($canonical)) {
                ksort($canonical);
            }

            return $canonical;
        }

        return is_int($value) ? (float) $value : $value;
    }
}
