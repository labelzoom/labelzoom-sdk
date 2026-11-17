<?php

declare(strict_types=1);

namespace LabelZoom\Sdk;

use LabelZoom\Sdk\Internal\ParameterWriters;
use LabelZoom\Sdk\Internal\PrintWriters;

/**
 * Prints a published template, one label per record. Returned by
 * {@see PrintRequestBuilder::fromTemplate()}.
 *
 * A template is already a label, so there are no source options. Each label is converted to the
 * printer's language.
 */
final class TemplatePrintBuilder
{
    /** @var list<object> */
    private array $records = [];

    private bool $validate = false;

    private ?int $copies = null;

    private ?string $idempotencyKey = null;

    /** @internal */
    public function __construct(
        private readonly LabelZoomClient $client,
        private readonly string $printerId,
        private readonly string $templateId,
    ) {
    }

    /**
     * The merge-field values: one record per label, whose keys are the template's merge fields. A
     * template with no merge fields needs none. A batch is limited by the plan's
     * {@see PrintQuotaLimits::getMaxBatchSize()}.
     *
     * A single record may be passed on its own; it is wrapped into a one-element array rather than
     * rejected (rule C3).
     *
     * @param array<string, mixed>|list<array<string, mixed>> $records
     */
    public function withData(array $records): self
    {
        $this->records = ParameterWriters::records($records);

        return $this;
    }

    /**
     * Checks every record for missing merge fields before anything prints. A missing field fails
     * the whole request with an {@see Exception\UnprocessableException}.
     */
    public function withValidate(bool $validate = true): self
    {
        $this->validate = $validate;

        return $this;
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
    public function send(): PrintBatch
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
