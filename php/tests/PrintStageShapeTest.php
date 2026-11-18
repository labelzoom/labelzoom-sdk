<?php

declare(strict_types=1);

namespace LabelZoom\Sdk\Tests;

use LabelZoom\Sdk\DynamicSourceBuilder;
use LabelZoom\Sdk\Exception\ValidationException;
use LabelZoom\Sdk\ImageSourceBuilder;
use LabelZoom\Sdk\LabelZoomClient;
use LabelZoom\Sdk\PdfSourceBuilder;
use LabelZoom\Sdk\PrintBatch;
use LabelZoom\Sdk\PrinterLanguageSourceBuilder;
use LabelZoom\Sdk\PrinterTargetBuilder;
use LabelZoom\Sdk\PrintJob;
use LabelZoom\Sdk\SourceBuilder;
use LabelZoom\Sdk\SourceFormat;
use LabelZoom\Sdk\SourceStage;
use LabelZoom\Sdk\SplitOptions;
use LabelZoom\Sdk\TemplatePrinterTargetBuilder;
use LabelZoom\Sdk\TemplateSourceBuilder;
use LabelZoom\Sdk\ZplSourceBuilder;
use PHPUnit\Framework\Attributes\DataProvider;
use PHPUnit\Framework\TestCase;

/**
 * Pins the printer target (contract section 1 and rule P2): a printer is reached from every source
 * stage through `toPrinter()`, and its stage offers only the options that apply to a printer.
 *
 * The conformance suite prints through {@see DynamicSourceBuilder}, because its fixtures pick
 * formats at run time. The tests here send through the typed builders, so that path is checked on
 * the wire too.
 */
final class PrintStageShapeTest extends TestCase
{
    private const SOURCE_STAGES = [
        SourceBuilder::class,
        PrinterLanguageSourceBuilder::class,
        ZplSourceBuilder::class,
        ImageSourceBuilder::class,
        PdfSourceBuilder::class,
        DynamicSourceBuilder::class,
    ];

    private const UUID_V4 = '/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/';

    /** @return iterable<string, array{class-string}> */
    public static function sourceStages(): iterable
    {
        foreach (self::SOURCE_STAGES as $stage) {
            yield $stage => [$stage];
        }
    }

    /** @param class-string $stage */
    #[DataProvider('sourceStages')]
    public function testEverySourceStageCanTargetAPrinter(string $stage): void
    {
        self::assertTrue(method_exists($stage, 'toPrinter'), "{$stage} must offer toPrinter");
        self::assertSame(PrinterTargetBuilder::class, self::returnType($stage, 'toPrinter'));
        // Declared once, on the base stage, so no source stage can drift from the others.
        self::assertSame(
            SourceStage::class,
            (new \ReflectionMethod($stage, 'toPrinter'))->getDeclaringClass()->getName(),
        );
    }

    public function testThePrinterTargetOffersOnlyPrinterOptions(): void
    {
        self::assertSame(
            ['execute', 'withCopies', 'withIdempotencyKey', 'withRotation'],
            self::methods(PrinterTargetBuilder::class),
        );
        self::assertSame(PrintJob::class, self::returnType(PrinterTargetBuilder::class, 'execute'));
    }

    public function testTheTemplateSourceTakesDataAndTargetsOnlyAPrinter(): void
    {
        self::assertSame(['toPrinter', 'withData', 'withValidate'], self::methods(TemplateSourceBuilder::class));
        self::assertSame(
            TemplatePrinterTargetBuilder::class,
            self::returnType(TemplateSourceBuilder::class, 'toPrinter'),
        );
        self::assertSame(
            ['execute', 'withCopies', 'withIdempotencyKey'],
            self::methods(TemplatePrinterTargetBuilder::class),
        );
        self::assertSame(PrintBatch::class, self::returnType(TemplatePrinterTargetBuilder::class, 'execute'));
    }

    public function testTheClientHasNoSeparatePrintEntry(): void
    {
        // Printing starts from convert(). A second entry point would be a second chain to keep in step.
        // Reflection rather than method_exists(): PHPStan proves the literal call false and rejects it.
        $methods = array_map(
            static fn (\ReflectionMethod $method): string => $method->getName(),
            (new \ReflectionClass(LabelZoomClient::class))->getMethods(\ReflectionMethod::IS_PUBLIC),
        );
        self::assertNotContains('print', $methods);
        self::assertContains('convert', $methods);
    }

    public function testAUrlAndBase64TextAreRefusedLocally(): void
    {
        $http = new MockHttpClient();
        $client = new LabelZoomClient(apiKey: 'lz_test', httpClient: $http);

        $stages = [
            'url' => $client->convert()->fromUrl('https://example.com/label.zpl'),
            'base64 text' => $client->convert()->fromBase64Text(SourceFormat::Pdf, 'JVBERi0='),
        ];
        foreach ($stages as $name => $stage) {
            try {
                $stage->toPrinter('p1');
                self::fail("Expected a ValidationException for {$name}.");
            } catch (ValidationException $e) {
                self::assertSame('source', $e->getParameter(), $name);
            }
        }
        self::assertSame([], $http->requests);
    }

    public function testTypedChainSendsSourceOptionsRotationCopiesAndKey(): void
    {
        $http = new MockHttpClient();
        $http->enqueue(200, '{"jobId":"j1","status":"queued","copies":2}', ['Content-Type' => 'application/json']);
        $client = new LabelZoomClient(apiKey: 'lz_test', maxRetries: 0, httpClient: $http);

        $job = $client->convert()
            ->fromPdf('%PDF')
                ->withPage(1)
                ->withSplit(static fn (SplitOptions $s) => $s->rows(2))
            ->toPrinter('shelf/1')
                ->withRotation(90)
                ->withCopies(2)
                ->withIdempotencyKey('order-42')
            ->execute();

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
            ['pdf' => ['pageNumber' => 1], 'split' => ['rows' => 2], 'rotation' => 90],
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

    public function testADesignFilePrintsAsItsOwnBytes(): void
    {
        $http = new MockHttpClient();
        $http->enqueue(200, '{"jobId":"j1","status":"queued"}', ['Content-Type' => 'application/json']);
        $client = new LabelZoomClient(apiKey: 'lz_test', maxRetries: 0, httpClient: $http);

        $client->convert()->fromBtw("\x00\xff\x10")->toPrinter('p1')->execute();

        $request = $http->lastRequest();
        parse_str($request->getUri()->getQuery(), $query);
        self::assertSame('btw', $query['sourceFormat'] ?? null);
        self::assertArrayNotHasKey('params', $query);
        self::assertSame('application/octet-stream', $request->getHeaderLine('Content-Type'));
        self::assertSame("\x00\xff\x10", (string) $request->getBody());
    }

    public function testEachCallGetsItsOwnGeneratedKey(): void
    {
        $http = new MockHttpClient();
        $http->enqueue(200, '{"jobId":"j1","status":"queued"}', ['Content-Type' => 'application/json']);
        $http->enqueue(200, '{"jobId":"j2","status":"queued"}', ['Content-Type' => 'application/json']);
        $client = new LabelZoomClient(apiKey: 'lz_test', maxRetries: 0, httpClient: $http);

        $print = $client->convert()->fromZpl('^XA^XZ')->toPrinter('printer-1');
        $print->execute();
        $print->execute();

        // Two calls are two prints. Only a retry within one call may reuse a key (rule P4).
        $first = $http->requests[0]->getHeaderLine('Idempotency-Key');
        $second = $http->requests[1]->getHeaderLine('Idempotency-Key');
        self::assertMatchesRegularExpression(self::UUID_V4, $first);
        self::assertMatchesRegularExpression(self::UUID_V4, $second);
        self::assertNotSame($first, $second);
    }

    public function testTypedTemplateChainSendsDataValidateCopiesAndKey(): void
    {
        $http = new MockHttpClient();
        $http->enqueue(200, '{"jobId":"j1","status":"queued"}', ['Content-Type' => 'application/json']);
        $client = new LabelZoomClient(apiKey: 'lz_test', maxRetries: 0, httpClient: $http);

        $batch = $client->convert()
            ->fromTemplate('tmpl-1')
                ->withData(['orderNumber' => 'SO-1001'])
                ->withValidate()
            ->toPrinter('printer-1')
                ->withCopies(3)
                ->withIdempotencyKey('batch-7')
            ->execute();

        $request = $http->lastRequest();
        self::assertSame('/api/v3/printers/printer-1/templates/tmpl-1/print', $request->getUri()->getPath());
        parse_str($request->getUri()->getQuery(), $query);
        self::assertSame(['copies' => '3', 'validate' => 'true'], $query);
        self::assertSame('application/json', $request->getHeaderLine('Content-Type'));
        self::assertSame('batch-7', $request->getHeaderLine('Idempotency-Key'));
        // One record is sent as that object, not as a one-element array (rule P3).
        self::assertSame('{"orderNumber":"SO-1001"}', (string) $request->getBody());
        self::assertCount(1, $batch->getJobs());
    }

    public function testAnEmptyTemplateIdIsRejectedLocally(): void
    {
        $http = new MockHttpClient();
        $client = new LabelZoomClient(apiKey: 'lz_test', httpClient: $http);

        try {
            $client->convert()->fromTemplate(' ');
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
