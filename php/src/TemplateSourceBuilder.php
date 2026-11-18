<?php

declare(strict_types=1);

namespace LabelZoom\Sdk;

use LabelZoom\Sdk\Internal\ParameterWriters;
use LabelZoom\Sdk\Internal\PrintWriters;

/**
 * The source stage for a published template. Returned by
 * {@see ConversionRequestBuilder::fromTemplate()}.
 *
 * A template is already a label, so it takes merge data instead of source options. Its only target
 * today is a printer.
 */
final class TemplateSourceBuilder
{
    /** @var list<object> */
    private array $records = [];

    private bool $validate = false;

    /** @internal */
    public function __construct(
        private readonly LabelZoomClient $client,
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

    /**
     * Prints one label per record on a Cloud Print printer, converted to its language.
     *
     * @param string $printerId the printer's id, from the Printers page
     *
     * @throws Exception\ValidationException if the printer id is empty
     */
    public function toPrinter(string $printerId): TemplatePrinterTargetBuilder
    {
        return new TemplatePrinterTargetBuilder(
            $this->client,
            $this->templateId,
            PrintWriters::requireId($printerId, 'printerId'),
            $this->records,
            $this->validate,
        );
    }
}
