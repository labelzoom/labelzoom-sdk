<?php

declare(strict_types=1);

namespace LabelZoom\Sdk;

use LabelZoom\Sdk\Internal\ConversionDraft;
use LabelZoom\Sdk\Internal\Json;
use LabelZoom\Sdk\Internal\ParameterWriters;
use LabelZoom\Sdk\Internal\PrintWriters;

/**
 * The printer target stage, after {@see SourceStage::toPrinter()}: where the document goes, how many
 * times, and the key that makes the print safe to repeat.
 *
 * The printer's configuration sets the output language, so the only target option is
 * {@see self::withRotation()}, which turns the finished label whatever the language.
 */
final class PrinterTargetBuilder
{
    private ?int $copies = null;

    private ?string $idempotencyKey = null;

    /** @internal */
    public function __construct(
        private readonly ConversionDraft $draft,
        private readonly string $printerId,
    ) {
    }

    /**
     * Rotation in degrees clockwise: 0, 90, 180 or 270. The server default is 0. It turns the
     * finished label, whatever the printer's language.
     */
    public function withRotation(int $rotation): self
    {
        ParameterWriters::rotation($this->draft, $rotation);

        return $this;
    }

    /**
     * Sends the whole job to the printer this many times, from 1 to 9999. It multiplies a quantity
     * in the document: `^PQ3` with 5 copies prints 15 labels.
     */
    public function withCopies(int $copies): self
    {
        $this->copies = PrintWriters::copies($copies);

        return $this;
    }

    /**
     * Makes the print safe to repeat: a second request with the same key returns the original job
     * instead of printing again. Use something unique to the label, such as an order number.
     *
     * Without it the SDK generates a key for each `execute()`, which makes its own retries safe but
     * not a repeat of the whole call. Keys are scoped to the account; a deliberate reprint needs a
     * new one.
     */
    public function withIdempotencyKey(string $key): self
    {
        $this->idempotencyKey = PrintWriters::idempotencyKey($key);

        return $this;
    }

    /**
     * Sends the document to the printer.
     *
     * The options are read when this is called, so a builder can be executed again, or changed and
     * executed again. Each call is a separate print with its own generated key, unless you set one.
     *
     * @throws Exception\LabelZoomException if the API returns a non-2xx response
     * @throws Exception\TransportException if no response was ever produced
     */
    public function execute(): PrintJob
    {
        $query = ['sourceFormat' => $this->draft->source->wireToken()];
        if ($this->copies !== null) {
            $query['copies'] = (string) $this->copies;
        }
        // An empty params would make the server convert a document that is already in the
        // printer's language, so it is sent only when an option was set (rule P2).
        if ($this->draft->params !== []) {
            $query['params'] = Json::encode($this->draft->params);
        }

        return PrintJob::fromJson($this->draft->client->requestJson(
            'POST',
            '/api/v3/printers/' . PrintWriters::segment($this->printerId) . '/print',
            $query,
            $this->draft->body,
            $this->draft->contentType,
            ['Idempotency-Key' => PrintWriters::keyForCall($this->idempotencyKey)],
        ));
    }
}
