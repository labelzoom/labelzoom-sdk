package com.labelzoom.sdk;

import java.util.Map;

/** The account's Cloud Print limits and usage, from {@link LabelZoomClient#getPrintQuota()}. */
public final class PrintQuota {

    private final String plan;
    private final PrintQuotaCounter agents;
    private final PrintQuotaCounter printers;
    private final PrintQuotaCounter jobsToday;
    private final PrintQuotaLimits limits;

    private PrintQuota(Map<String, Object> json) {
        String name = PrintResponses.string(json, "plan");
        this.plan = name == null ? "" : name;
        this.agents = new PrintQuotaCounter(PrintResponses.object(json, "agents"));
        this.printers = new PrintQuotaCounter(PrintResponses.object(json, "printers"));
        this.jobsToday = new PrintQuotaCounter(PrintResponses.object(json, "jobsToday"));
        this.limits = new PrintQuotaLimits(PrintResponses.object(json, "limits"));
    }

    static PrintQuota fromJson(Map<String, Object> json) {
        return new PrintQuota(json);
    }

    /** The account's print plan. */
    public String plan() {
        return plan;
    }

    /** Enrolled agents. Unused enrollment codes count in {@link PrintQuotaCounter#pending()}. */
    public PrintQuotaCounter agents() {
        return agents;
    }

    /** Printers added. */
    public PrintQuotaCounter printers() {
        return printers;
    }

    /** Jobs created today. The count resets at {@link PrintQuotaCounter#resetAt()}, 00:00 UTC. */
    public PrintQuotaCounter jobsToday() {
        return jobsToday;
    }

    /** The size limits for a print request. */
    public PrintQuotaLimits limits() {
        return limits;
    }
}
