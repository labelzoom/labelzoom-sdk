// A few conformance cases set process-wide environment variables to exercise the
// LABELZOOM_API_KEY fallback, and process state is not something xUnit can isolate per
// collection. Serializing the assembly is the cheap, honest fix; the suite is offline and
// finishes in well under a second either way.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
