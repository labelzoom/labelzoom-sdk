<?php

declare(strict_types=1);

namespace LabelZoom\Sdk;

use LabelZoom\Sdk\Internal\PrintWriters;

/**
 * The printer target stage for a template, after {@see TemplateSourceBuilder::toPrinter()}.
 */
final class TemplatePrinterTargetBuilder
{
    private ?int $copies = null;

    private ?string $idempotencyKey = null;

    /**
     * @internal
     *
     * @param list<object> $records
     */
    public function __construct(
        private readonly LabelZoomClient $client,
        private readonly string $templateId,
        private readonly string $printerId,
        private readonly array $records,
        private readonly bool $validate,
    ) {
    }

    /** Sends each label to the printer this many times, from 1 to 9999. */
    public function withCopies(int $copies): self
    {
        $this->copies = PrintWriters::copies($copies);

        return $this;
    }

    /**
     * Makes the print safe to repeat. In a batch the server gives each record its own key, derived
     * from this one, so a repeated batch prints only the records that didn't print before.
     */
    public function withIdempotencyKey(string $key): self
    {
        $this->idempotencyKey = PrintWriters::idempotencyKey($key);

        return $this;
    }

    /**
     * Sends the labels to the printer.
     *
     * If {@see PrintBatch::getRemaining()} isn't 0, send the same request again with the same
     * Idempotency-Key to submit the rest.
     *
     * @throws Exception\LabelZoomException if the API returns a non-2xx response
     * @throws Exception\TransportException if no response was ever produced
     */
    public function execute(): PrintBatch
    {
        $query = [];
        if ($this->copies !== null) {
            $query['copies'] = (string) $this->copies;
        }
        if ($this->validate) {
            $query['validate'] = 'true';
        }

        return PrintBatch::fromJson($this->client->requestJson(
            'POST',
            '/api/v3/printers/' . PrintWriters::segment($this->printerId)
                . '/templates/' . PrintWriters::segment($this->templateId) . '/print',
            $query,
            PrintWriters::templateBody($this->records),
            PrintWriters::JSON_MEDIA_TYPE,
            // One key for the whole batch: the server derives each entry's own from it (rule P4).
            ['Idempotency-Key' => PrintWriters::keyForCall($this->idempotencyKey)],
        ));
    }
}
