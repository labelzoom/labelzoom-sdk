<?php

declare(strict_types=1);

namespace LabelZoom\Sdk;

use LabelZoom\Sdk\Internal\ConversionDraft;
use LabelZoom\Sdk\Internal\Json;
use LabelZoom\Sdk\Internal\ParameterWriters;
use LabelZoom\Sdk\Internal\PrintWriters;

/**
 * The print source stage: options that change how the document is **read**, then the print
 * options, then `send()`.
 *
 * It mirrors {@see SourceStage} without the `to*` transitions, because the printer picks the output
 * language. Each format's print builder offers exactly the source options its convert builder does,
 * through the same shared writers, and a stage-shape test holds the two sets equal (contract section
 * 1.2). An option the server doesn't apply to the format is ignored rather than rejected.
 */
abstract class PrintStage
{
    private ?int $copies = null;

    private ?string $idempotencyKey = null;

    /** @internal */
    public function __construct(
        protected readonly ConversionDraft $draft,
        private readonly string $printerId,
    ) {
    }

    /**
     * Label dimensions **in inches**. They replace whatever size the document declares; content
     * past them is clipped, as it is on the printer. Omit this to use the declared size, or to
     * detect one from the content.
     */
    public function withLabelSize(float $widthInches, float $heightInches): static
    {
        ParameterWriters::labelSize($this->draft, $widthInches, $heightInches);

        return $this;
    }

    /** Prints only one label of a multi-label document, identified by a **0-based** index. */
    public function withLabelIndex(int $zeroBasedIndex): static
    {
        ParameterWriters::labelIndex($this->draft, $zeroBasedIndex);

        return $this;
    }

    /**
     * Supplies data to fill the document's variable fields. **Each record prints one label.**
     *
     * A single record may be passed on its own; it is wrapped into a one-element array rather than
     * rejected (rule C3).
     *
     * @param array<string, mixed>|list<array<string, mixed>> $records
     */
    public function withData(array $records): static
    {
        ParameterWriters::data($this->draft, $records);

        return $this;
    }

    /**
     * Sends the whole job to the printer this many times, from 1 to 9999. It multiplies a quantity
     * in the document: `^PQ3` with 5 copies prints 15 labels.
     */
    public function withCopies(int $copies): static
    {
        $this->copies = PrintWriters::copies($copies);

        return $this;
    }

    /**
     * Makes the print safe to repeat: a second request with the same key returns the original job
     * instead of printing again. Use something unique to the label, such as an order number.
     *
     * Without it the SDK generates a key for each `send()`, which makes its own retries safe but
     * not a repeat of the whole call. Keys are scoped to the account; a deliberate reprint needs a
     * new one.
     */
    public function withIdempotencyKey(string $key): static
    {
        $this->idempotencyKey = PrintWriters::idempotencyKey($key);

        return $this;
    }

    /**
     * Sends the document to the printer.
     *
     * The options are read when this is called, so a builder can be sent again, or changed and sent
     * again. Each call is a separate print with its own generated key, unless you set one.
     *
     * @throws Exception\LabelZoomException if the API returns a non-2xx response
     * @throws Exception\TransportException if no response was ever produced
     */
    public function send(): PrintJob
    {
        $query = ['sourceFormat' => $this->draft->source->wireToken()];
        if ($this->copies !== null) {
            $query['copies'] = (string) $this->copies;
        }
        // An empty params would make the server convert a document that is already in the
        // printer's language, so it is sent only when a source option was set (rule P2).
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
