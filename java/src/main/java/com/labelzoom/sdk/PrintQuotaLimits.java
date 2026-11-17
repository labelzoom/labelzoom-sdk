package com.labelzoom.sdk;

import java.util.Map;

/** The size limits for a print request. */
public final class PrintQuotaLimits {

    private final long maxPayloadBytes;
    private final long maxStoredPayloadBytes;
    private final int maxBatchSize;

    PrintQuotaLimits(Map<String, Object> json) {
        Long payload = PrintResponses.longValue(json, "maxPayloadBytes");
        Long stored = PrintResponses.longValue(json, "maxStoredPayloadBytes");
        Integer batch = PrintResponses.integer(json, "maxBatchSize");
        this.maxPayloadBytes = payload == null ? 0 : payload;
        this.maxStoredPayloadBytes = stored == null ? 0 : stored;
        this.maxBatchSize = batch == null ? 0 : batch;
    }

    /** The largest document a print request can send. */
    public long maxPayloadBytes() {
        return maxPayloadBytes;
    }

    /** The largest document after conversion to the printer's language. */
    public long maxStoredPayloadBytes() {
        return maxStoredPayloadBytes;
    }

    /** The most records one template print can send. */
    public int maxBatchSize() {
        return maxBatchSize;
    }
}
