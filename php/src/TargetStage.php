<?php

declare(strict_types=1);

namespace LabelZoom\Sdk;

use LabelZoom\Sdk\Internal\ConversionDraft;
use LabelZoom\Sdk\Internal\ParameterWriters;

/**
 * The target stage: options that change the **output**, followed by `execute()`.
 *
 * The options here apply to every target. Format-specific ones live on the builder that `to*`
 * returns for that format, such as {@see ZplTargetBuilder}. How an image or PDF source is
 * rasterized is a source option: see {@see RasterOptions}. An option the server doesn't apply to the
 * chosen format pair is ignored rather than rejected.
 */
abstract class TargetStage
{
    /** @internal */
    public function __construct(
        protected readonly ConversionDraft $draft,
        private readonly TargetFormat $target,
    ) {
    }

    /**
     * Rotation in degrees clockwise: 0, 90, 180 or 270. The server default is 0. It turns the
     * finished label, for every format pair.
     */
    public function withRotation(int $rotation): static
    {
        ParameterWriters::rotation($this->draft, $rotation);

        return $this;
    }

    /** Requests a watermark. Output is watermarked regardless on the anonymous free tier. */
    public function withWatermark(bool $watermark = true): static
    {
        $this->draft->params['watermark'] = $watermark;

        return $this;
    }

    /**
     * Selects a printer dialect, for example `moca` for Blue Yonder WMS. Requires a paid license;
     * without one the request fails with a 403 whose `isPaidFeature()` is set.
     */
    public function withDialect(string $dialect): static
    {
        ParameterWriters::dialect($this->draft, $dialect);

        return $this;
    }

    /**
     * Sets a parameter the SDK does not model yet.
     *
     * Unknown keys are ignored by the server, so this is a safe forward-compatibility hatch.
     */
    public function withParameter(string $key, mixed $value): static
    {
        ParameterWriters::parameter($this->draft, $key, $value);

        return $this;
    }

    /** Adds a raw query-string parameter alongside `params`. */
    public function withRawQueryParameter(string $key, string $value): static
    {
        ParameterWriters::rawQueryParameter($this->draft, $key, $value);

        return $this;
    }

    /**
     * Executes the conversion. The options are copied when this is called, so a builder can be
     * executed again, or changed and executed again, without affecting a request in flight.
     *
     * @throws Exception\LabelZoomException if the API returns a non-2xx response
     * @throws Exception\TransportException if no response was ever produced
     */
    public function execute(): ConversionResult
    {
        return $this->draft->client->execute(
            $this->draft->source,
            $this->target,
            $this->draft->body,
            $this->draft->contentType,
            $this->draft->params,
            $this->draft->rawQuery,
        );
    }
}
