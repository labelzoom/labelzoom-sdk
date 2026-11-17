<?php

declare(strict_types=1);

namespace LabelZoom\Sdk\Tests;

use LabelZoom\Sdk\ColorMode;
use LabelZoom\Sdk\DynamicSourceBuilder;
use LabelZoom\Sdk\DynamicTargetBuilder;
use LabelZoom\Sdk\Exception\ValidationException;
use LabelZoom\Sdk\ImageSourceBuilder;
use LabelZoom\Sdk\ImageTargetBuilder;
use LabelZoom\Sdk\LabelZoomClient;
use LabelZoom\Sdk\LayoutOptions;
use LabelZoom\Sdk\PdfSourceBuilder;
use LabelZoom\Sdk\PdfTargetBuilder;
use LabelZoom\Sdk\PrinterLanguageSourceBuilder;
use LabelZoom\Sdk\RasterOptions;
use LabelZoom\Sdk\SourceBuilder;
use LabelZoom\Sdk\SourceFormat;
use LabelZoom\Sdk\SplitOptions;
use LabelZoom\Sdk\TargetBuilder;
use LabelZoom\Sdk\ZplImageCompression;
use LabelZoom\Sdk\ZplSourceBuilder;
use LabelZoom\Sdk\ZplTargetBuilder;
use PHPUnit\Framework\Attributes\DataProvider;
use PHPUnit\Framework\TestCase;

/**
 * Pins which options each stage of the fluent chain offers.
 *
 * The staged builders exist so that an option can only be set where it acts:
 * `fromZpl($zpl)->withPage(0)` must fail static analysis, and so must `toPdf()->withDpi(300)`.
 * These assert what makes that failure certain: the method isn't a public member of that builder.
 *
 * The conformance suite runs through the dynamic builders, because its fixtures pick formats at run
 * time. The last tests here send requests through the typed builders, so that path is checked on the
 * wire too.
 */
final class StageShapeTest extends TestCase
{
    private const SOURCE_STAGES = [
        SourceBuilder::class,
        PrinterLanguageSourceBuilder::class,
        ZplSourceBuilder::class,
        ImageSourceBuilder::class,
        PdfSourceBuilder::class,
        DynamicSourceBuilder::class,
    ];

    private const TARGET_STAGES = [
        TargetBuilder::class,
        PdfTargetBuilder::class,
        ZplTargetBuilder::class,
        ImageTargetBuilder::class,
        DynamicTargetBuilder::class,
    ];

    /** @return iterable<string, array{class-string, string}> */
    public static function absent(): iterable
    {
        yield 'zpl has no page' => [ZplSourceBuilder::class, 'withPage'];
        yield 'zpl has no split' => [ZplSourceBuilder::class, 'withSplit'];
        yield 'zpl has no raster' => [ZplSourceBuilder::class, 'withRaster'];
        yield 'printer language has no raster' => [PrinterLanguageSourceBuilder::class, 'withRaster'];
        yield 'xml has no dpi' => [SourceBuilder::class, 'withDpi'];
        yield 'pdf source has no dpi' => [PdfSourceBuilder::class, 'withDpi'];
        yield 'pdf source has no commands' => [PdfSourceBuilder::class, 'withCommandsToIgnore'];
        yield 'image has no page' => [ImageSourceBuilder::class, 'withPage'];
        yield 'pdf target has no dpi' => [PdfTargetBuilder::class, 'withDpi'];
        yield 'plain target has no layout' => [TargetBuilder::class, 'withLayout'];
        yield 'plain target has no color mode' => [TargetBuilder::class, 'withColorMode'];
        yield 'zpl target has no color mode' => [ZplTargetBuilder::class, 'withColorMode'];
        yield 'image target has no layout' => [ImageTargetBuilder::class, 'withLayout'];
    }

    /** @param class-string $builder */
    #[DataProvider('absent')]
    public function testFormatSpecificOptionIsAbsentFromOtherFormats(string $builder, string $method): void
    {
        self::assertFalse(method_exists($builder, $method), "{$builder} must not offer {$method}");
    }

    /** @return iterable<string, array{class-string, string}> */
    public static function present(): iterable
    {
        yield 'zpl dpi' => [ZplSourceBuilder::class, 'withDpi'];
        yield 'zpl commands' => [ZplSourceBuilder::class, 'withCommandsToIgnore'];
        yield 'printer language dpi' => [PrinterLanguageSourceBuilder::class, 'withDpi'];
        yield 'image dpi' => [ImageSourceBuilder::class, 'withDpi'];
        yield 'image split' => [ImageSourceBuilder::class, 'withSplit'];
        yield 'image raster' => [ImageSourceBuilder::class, 'withRaster'];
        yield 'pdf page' => [PdfSourceBuilder::class, 'withPage'];
        yield 'pdf raster' => [PdfSourceBuilder::class, 'withRaster'];
        yield 'dynamic source raster' => [DynamicSourceBuilder::class, 'withRaster'];
        yield 'plain target dpi' => [TargetBuilder::class, 'withDpi'];
        yield 'zpl target layout' => [ZplTargetBuilder::class, 'withLayout'];
        yield 'image target color mode' => [ImageTargetBuilder::class, 'withColorMode'];
        yield 'dynamic target color mode' => [DynamicTargetBuilder::class, 'withColorMode'];
    }

    /** @param class-string $builder */
    #[DataProvider('present')]
    public function testFormatSpecificOptionIsOnItsFormat(string $builder, string $method): void
    {
        self::assertTrue(method_exists($builder, $method), "{$builder} must offer {$method}");
    }

    public function testSourceStagesOfferNoTargetOptions(): void
    {
        foreach (self::SOURCE_STAGES as $stage) {
            foreach (['withRotation', 'withDialect', 'withWatermark', 'withLayout', 'execute'] as $method) {
                self::assertFalse(method_exists($stage, $method), "{$stage} must not offer {$method}");
            }
        }
    }

    public function testTargetStagesOfferNoSourceOptions(): void
    {
        $methods = [
            'withLabelSize',
            'withLabelIndex',
            'withData',
            'withPage',
            'withPdfPage',
            'withSplit',
            'withRaster',
            'withScaling',
            'withPosition',
            'withZplCommandsToIgnore',
        ];
        foreach (self::TARGET_STAGES as $stage) {
            foreach ($methods as $method) {
                self::assertFalse(method_exists($stage, $method), "{$stage} must not offer {$method}");
            }
        }
    }

    public function testNamedMethodsReturnTheStageForTheirFormat(): void
    {
        $convert = (new LabelZoomClient(apiKey: null, httpClient: new MockHttpClient()))->convert();
        self::assertInstanceOf(ZplSourceBuilder::class, $convert->fromZpl('^XA^XZ'));
        self::assertInstanceOf(PdfSourceBuilder::class, $convert->fromPdf('%PDF'));
        self::assertInstanceOf(ImageSourceBuilder::class, $convert->fromPng("\x89PNG"));
        self::assertInstanceOf(PrinterLanguageSourceBuilder::class, $convert->fromEpl('N'));
        self::assertInstanceOf(SourceBuilder::class, $convert->fromXml('<labelZoomXml/>'));
        self::assertInstanceOf(DynamicSourceBuilder::class, $convert->from(SourceFormat::Zpl, '^XA^XZ'));
        self::assertInstanceOf(ZplTargetBuilder::class, $convert->fromZpl('^XA^XZ')->toZpl());
        self::assertInstanceOf(ImageTargetBuilder::class, $convert->fromZpl('^XA^XZ')->toPng());
        self::assertInstanceOf(PdfTargetBuilder::class, $convert->fromZpl('^XA^XZ')->toPdf());
        self::assertInstanceOf(TargetBuilder::class, $convert->fromZpl('^XA^XZ')->toEpl());
    }

    public function testTypedChainSendsTheSameParamsAsTheWire(): void
    {
        $params = self::send(
            static fn (LabelZoomClient $client) => $client->convert()
                ->fromPdf('%PDF')
                    ->withPage(0)
                    ->withSplit(static fn (SplitOptions $s) => $s->rows(2)->columns(2)->skipBlank())
                    ->withRaster(
                        static fn (RasterOptions $r) => $r->scaling(150)->position(-20, 5)->darkness(60)
                            ->colorMode(ColorMode::Bw),
                    )
                ->toZpl()
                    ->withDpi(203)
                    ->withRotation(90)
                    ->withCollapseCopies()
                    ->withLayout(static fn (LayoutOptions $l) => $l->across(2)->gap(0.125))
                ->execute(),
            '/api/v2/convert/pdf/to/zpl',
        );

        self::assertSame(['pageNumber' => 0], $params['pdf']);
        self::assertSame(['rows' => 2, 'columns' => 2, 'skipBlank' => true], $params['split']);
        self::assertEquals(150, $params['scaling']);
        self::assertSame(['x' => -20, 'y' => 5], $params['position']);
        self::assertSame(60, $params['darkness']);
        self::assertSame('BW', $params['colorMode']);
        self::assertSame(203, $params['targetDpi']);
        self::assertSame(90, $params['rotation']);
        self::assertSame(['collapseCopies' => true], $params['zpl']);
        self::assertEquals(['across' => 2, 'gap' => 0.125], $params['layout']);
    }

    public function testEachStageSendsItsOwnDpi(): void
    {
        $params = self::send(
            static fn (LabelZoomClient $client) => $client->convert()
                ->fromZpl('^XA^XZ')->withDpi(300)
                ->toZpl()->withDpi(203)
                ->execute(),
            '/api/v2/convert/zpl/to/zpl',
        );

        self::assertSame(300, $params['sourceDpi']);
        self::assertSame(203, $params['targetDpi']);
        self::assertArrayNotHasKey('dpi', $params, 'the legacy dpi key must never be sent (contract C8)');
    }

    public function testAGroupSetOnBothStagesIsMergedRatherThanReplaced(): void
    {
        $params = self::send(
            static fn (LabelZoomClient $client) => $client->convert()
                ->fromZpl('^XA^XZ')->withCommandsToIgnore(['^PQ'])
                ->toZpl()->withImageCompression(ZplImageCompression::CompressedHex)
                ->execute(),
            '/api/v2/convert/zpl/to/zpl',
        );

        self::assertSame(['commandsToIgnore' => ['^PQ'], 'imageCompression' => 'COMPRESSED_HEX'], $params['zpl']);
    }

    public function testRasterValidationStillNamesTheParameter(): void
    {
        try {
            (new LabelZoomClient(apiKey: null, httpClient: new MockHttpClient()))->convert()
                ->fromPng("\x89PNG")
                ->withRaster(static fn (RasterOptions $r) => $r->darkness(101));
            self::fail('Expected a ValidationException.');
        } catch (ValidationException $e) {
            self::assertSame('darkness', $e->getParameter());
        }
    }

    /**
     * @param callable(LabelZoomClient): mixed $call
     *
     * @return array<string, mixed>
     */
    private static function send(callable $call, string $expectedPath): array
    {
        $http = new MockHttpClient();
        $http->enqueue(200, '^XA^XZ', ['Content-Type' => 'text/plain']);
        $call(new LabelZoomClient(apiKey: null, maxRetries: 0, httpClient: $http));

        $uri = $http->lastRequest()->getUri();
        self::assertSame($expectedPath, $uri->getPath());
        parse_str($uri->getQuery(), $query);
        self::assertIsString($query['params'] ?? null);
        /** @var array<string, mixed> $params */
        $params = json_decode($query['params'], true, 512, JSON_THROW_ON_ERROR);

        return $params;
    }
}
