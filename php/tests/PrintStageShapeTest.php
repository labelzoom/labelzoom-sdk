<?php

declare(strict_types=1);

namespace LabelZoom\Sdk\Tests;

use LabelZoom\Sdk\ConversionRequestBuilder;
use LabelZoom\Sdk\DynamicPrintBuilder;
use LabelZoom\Sdk\DynamicSourceBuilder;
use LabelZoom\Sdk\Exception\ValidationException;
use LabelZoom\Sdk\ImagePrintBuilder;
use LabelZoom\Sdk\ImageSourceBuilder;
use LabelZoom\Sdk\LabelZoomClient;
use LabelZoom\Sdk\PdfPrintBuilder;
use LabelZoom\Sdk\PdfSourceBuilder;
use LabelZoom\Sdk\PrintBuilder;
use LabelZoom\Sdk\PrinterLanguagePrintBuilder;
use LabelZoom\Sdk\PrinterLanguageSourceBuilder;
use LabelZoom\Sdk\PrintRequestBuilder;
use LabelZoom\Sdk\SourceBuilder;
use LabelZoom\Sdk\SplitOptions;
use LabelZoom\Sdk\ZplPrintBuilder;
use LabelZoom\Sdk\ZplSourceBuilder;
use PHPUnit\Framework\Attributes\DataProvider;
use PHPUnit\Framework\TestCase;

/**
 * Pins the print stage against the convert source stage it mirrors (contract section 1.2).
 *
 * The print builders can't reuse the convert source builders, because those carry the `to*`
 * transitions and print has no target stage. So each format has a print twin. These tests are
 * what stop the twins drifting: a source option added to {@see PdfSourceBuilder} and not to
 * {@see PdfPrintBuilder} fails here.
 *
 * The conformance suite prints through {@see DynamicPrintBuilder}, because its fixtures pick
 * formats at run time. The last tests here send through the typed builders, so that path is
 * checked on the wire too.
 */
final class PrintStageShapeTest extends TestCase
{
    /** Convert source stage => its print twin. */
    private const TWINS = [
        SourceBuilder::class => PrintBuilder::class,
        PrinterLanguageSourceBuilder::class => PrinterLanguagePrintBuilder::class,
        ZplSourceBuilder::class => ZplPrintBuilder::class,
        ImageSourceBuilder::class => ImagePrintBuilder::class,
        PdfSourceBuilder::class => PdfPrintBuilder::class,
        DynamicSourceBuilder::class => DynamicPrintBuilder::class,
    ];

    /** The methods a print stage has and a convert source stage doesn't. */
    private const PRINT_ONLY = ['withCopies', 'withIdempotencyKey', 'send'];

    /** The convert `from*` methods with no print twin: print doesn't convert these (rule P2). */
    private const NOT_PRINTABLE = [
        'fromBase64Text',
        'fromUrl',
        'fromEscLabel',
        'fromEzpl',
        'fromTpcl',
        'fromNlbl',
        'fromLbl',
        'fromBtw',
        'fromDymo',
    ];

    private const UUID_V4 = '/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/';

    /** @return iterable<string, array{class-string, class-string}> */
    public static function twins(): iterable
    {
        foreach (self::TWINS as $convert => $print) {
            yield $print => [$convert, $print];
        }
    }

    /**
     * @param class-string $convert
     * @param class-string $print
     */
    #[DataProvider('twins')]
    public function testPrintTwinOffersExactlyTheConvertSourceOptions(string $convert, string $print): void
    {
        $convertOptions = array_values(array_filter(
            self::methods($convert),
            static fn (string $name): bool => !str_starts_with($name, 'to'),
        ));
        $printOptions = array_values(array_filter(
            self::methods($print),
            static fn (string $name): bool => !in_array($name, self::PRINT_ONLY, true),
        ));

        self::assertSame($convertOptions, $printOptions, "{$print} must offer exactly {$convert}'s source options");
    }

    /**
     * @param class-string $convert
     * @param class-string $print
     */
    #[DataProvider('twins')]
    public function testPrintTwinHasNoTargetStage(string $convert, string $print): void
    {
        foreach (self::methods($print) as $name) {
            self::assertFalse(str_starts_with($name, 'to'), "{$print} must not offer {$name}");
        }
        self::assertNotContains('execute', self::methods($print));
        foreach (self::PRINT_ONLY as $name) {
            self::assertContains($name, self::methods($print));
        }
    }

    public function testPrintOffersFromMethodsOnlyForPrintableFormats(): void
    {
        $convert = array_values(array_filter(
            self::methods(ConversionRequestBuilder::class),
            static fn (string $name): bool => !in_array($name, self::NOT_PRINTABLE, true),
        ));
        $print = array_values(array_filter(
            self::methods(PrintRequestBuilder::class),
            static fn (string $name): bool => $name !== 'fromTemplate',
        ));

        self::assertSame($convert, $print);
        self::assertContains('fromTemplate', self::methods(PrintRequestBuilder::class));
    }

    public function testEachFromMethodReturnsTheTwinOfItsConvertStage(): void
    {
        foreach (self::methods(PrintRequestBuilder::class) as $name) {
            if ($name === 'fromTemplate') {
                continue;
            }
            $convertStage = self::returnType(ConversionRequestBuilder::class, $name);
            self::assertArrayHasKey($convertStage, self::TWINS, "convert {$name}() returns {$convertStage}");
            self::assertSame(
                self::TWINS[$convertStage],
                self::returnType(PrintRequestBuilder::class, $name),
                "print {$name}() must return the twin of convert {$name}()",
            );
        }
    }

    public function testTypedPrintChainSendsSourceOptionsCopiesAndKey(): void
    {
        $http = new MockHttpClient();
        $http->enqueue(200, '{"jobId":"j1","status":"queued","copies":2}', ['Content-Type' => 'application/json']);
        $client = new LabelZoomClient(apiKey: 'lz_test', maxRetries: 0, httpClient: $http);

        $job = $client->print('shelf/1')
            ->fromPdf('%PDF')
                ->withPage(1)
                ->withSplit(static fn (SplitOptions $s) => $s->rows(2))
            ->withCopies(2)
            ->withIdempotencyKey('order-42')
            ->send();

        $request = $http->lastRequest();
        $uri = $request->getUri();
        self::assertSame('POST', $request->getMethod());
        // The id is one path segment, so its slash is encoded (rule P1).
        self::assertSame('/api/v3/printers/shelf%2F1/print', $uri->getPath());
        parse_str($uri->getQuery(), $query);
        self::assertSame('pdf', $query['sourceFormat'] ?? null);
        self::assertSame('2', $query['copies'] ?? null);
        self::assertIsString($query['params'] ?? null);
        self::assertSame(
            ['pdf' => ['pageNumber' => 1], 'split' => ['rows' => 2]],
            json_decode($query['params'], true, 512, JSON_THROW_ON_ERROR),
        );
        self::assertSame('order-42', $request->getHeaderLine('Idempotency-Key'));
        self::assertSame('application/pdf', $request->getHeaderLine('Content-Type'));
        self::assertSame('application/json', $request->getHeaderLine('Accept'));
        self::assertSame('%PDF', (string) $request->getBody());

        self::assertSame('j1', $job->getJobId());
        self::assertSame('queued', $job->getStatus());
        self::assertSame(2, $job->getCopies());
        self::assertFalse($job->isIdempotent());
    }

    public function testEachCallGetsItsOwnGeneratedKey(): void
    {
        $http = new MockHttpClient();
        $http->enqueue(200, '{"jobId":"j1","status":"queued"}', ['Content-Type' => 'application/json']);
        $http->enqueue(200, '{"jobId":"j2","status":"queued"}', ['Content-Type' => 'application/json']);
        $client = new LabelZoomClient(apiKey: 'lz_test', maxRetries: 0, httpClient: $http);

        $print = $client->print('printer-1')->fromZpl('^XA^XZ');
        $print->send();
        $print->send();

        // Two calls are two prints. Only a retry within one call may reuse a key (rule P4).
        $first = $http->requests[0]->getHeaderLine('Idempotency-Key');
        $second = $http->requests[1]->getHeaderLine('Idempotency-Key');
        self::assertMatchesRegularExpression(self::UUID_V4, $first);
        self::assertMatchesRegularExpression(self::UUID_V4, $second);
        self::assertNotSame($first, $second);
    }

    public function testAnEmptyTemplateIdIsRejectedLocally(): void
    {
        $http = new MockHttpClient();
        $client = new LabelZoomClient(apiKey: 'lz_test', httpClient: $http);

        try {
            $client->print('printer-1')->fromTemplate(' ');
            self::fail('Expected a ValidationException.');
        } catch (ValidationException $e) {
            self::assertSame('templateId', $e->getParameter());
        }
        self::assertSame([], $http->requests);
    }

    /**
     * The public instance methods a class offers, sorted.
     *
     * @param class-string $class
     *
     * @return list<string>
     */
    private static function methods(string $class): array
    {
        $names = [];
        foreach ((new \ReflectionClass($class))->getMethods(\ReflectionMethod::IS_PUBLIC) as $method) {
            if ($method->isStatic() || $method->isConstructor()) {
                continue;
            }
            $names[] = $method->getName();
        }
        $names = array_values(array_unique($names));
        sort($names);

        return $names;
    }

    /**
     * @param class-string $class
     */
    private static function returnType(string $class, string $method): string
    {
        $type = (new \ReflectionMethod($class, $method))->getReturnType();
        self::assertInstanceOf(\ReflectionNamedType::class, $type, "{$class}::{$method}() declares one return type");

        return $type->getName();
    }
}
