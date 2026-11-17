using System.Net;
using System.Text;
using System.Text.Json;
using LabelZoom.Sdk.Conversion;
using LabelZoom.Sdk.Printing;

namespace LabelZoom.Sdk.Tests.Conformance;

/// <summary>
/// Runs the shared conformance fixtures against the .NET SDK.
/// </summary>
/// <remarks>
/// Entirely offline — every case is served by <see cref="StubHttpMessageHandler"/>. No credential,
/// no network, so this passes identically on a fork pull request with no secrets configured.
/// </remarks>
public sealed class ConformanceTests
{
    [Theory]
    [MemberData(nameof(ConformanceFixtures.TheoryData), MemberType = typeof(ConformanceFixtures))]
    public async Task Case(string caseId)
    {
        var fixture = ConformanceFixtures.Load(caseId);
        var given = fixture.GetProperty("given");
        var expect = fixture.GetProperty("expect");

        var task = caseId.Split('/')[0] switch
        {
            "request" => RunRequestCase(given, expect),
            "response" => RunResponseCase(given, expect),
            "retry" => RunRetryCase(given, expect),
            "validation" => RunValidationCase(given, expect),
            "typecheck" => RunTypecheckCase(given),
            "print" => RunPrintCase(given, expect),
            var kind => throw new InvalidOperationException($"Unknown case kind '{kind}'."),
        };

        await task;
    }

    /// <summary>
    /// Asserts this suite covers every case the contract declares.
    /// </summary>
    /// <remarks>
    /// This is the whole anti-drift mechanism. A suite that quietly runs a subset of the fixtures
    /// reports success exactly like one that runs all of them, so the coverage itself is asserted
    /// rather than assumed. See docs/CONFORMANCE.md section 4.
    /// </remarks>
    [Fact]
    public void Suite_CoversEveryDeclaredCase()
    {
        var all = ConformanceFixtures.AllCaseIds.ToHashSet();

        foreach (var skipped in ConformanceFixtures.Skips)
        {
            Assert.True(
                all.Contains(skipped.Key),
                $"skips/dotnet.json declares '{skipped.Key}', which is not a case in spec.json.");
            Assert.False(
                string.IsNullOrWhiteSpace(skipped.Value),
                $"Skip '{skipped.Key}' has no reason.");
        }

        var expected = all.Except(ConformanceFixtures.Skips.Keys).OrderBy(x => x, StringComparer.Ordinal);
        var actual = ConformanceFixtures.ExpectedCaseIds.OrderBy(x => x, StringComparer.Ordinal);

        Assert.Equal(expected, actual);

        // .NET declares no skips: it is a statically typed language, so even the typecheck cases
        // are checkable here (via the enum surface). If that ever changes, the skip needs a reason.
        Assert.Empty(ConformanceFixtures.Skips);
    }

    // ------------------------------------------------------------------ request

    private async Task RunRequestCase(JsonElement given, JsonElement expect)
    {
        var handler = new StubHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, "^XA^XZ", "text/plain");

        using var scope = EnvironmentScope.From(given);
        using var client = BuildClient(given, handler);

        await BuildRequest(client, given).ExecuteAsync();

        AssertRequest(handler.LastRequest, expect);
    }

    /// <summary>Asserts a recorded request against the request vocabulary (CONFORMANCE.md section 2).</summary>
    private static void AssertRequest(RecordedRequest request, JsonElement expect)
    {
        if (expect.TryGetProperty("method", out var method))
        {
            Assert.Equal(method.GetString(), request.Method);
        }

        if (expect.TryGetProperty("url", out var url))
        {
            Assert.Equal(url.GetString(), request.RequestUri.GetLeftPart(UriPartial.Path));
        }

        if (expect.TryGetProperty("path", out var path))
        {
            Assert.Equal(path.GetString(), request.RequestUri.AbsolutePath);
        }

        if (expect.TryGetProperty("headers", out var headers))
        {
            foreach (var header in headers.EnumerateObject())
            {
                Assert.True(
                    request.HasHeader(header.Name),
                    $"Expected header '{header.Name}' but the request did not send it.");
                Assert.Equal(header.Value.GetString(), request.Header(header.Name));
            }
        }

        foreach (var absent in EnumerateStrings(expect, "headersAbsent"))
        {
            Assert.False(
                request.HasHeader(absent),
                $"Header '{absent}' must be absent, but was sent as '{request.Header(absent)}'.");
        }

        if (expect.TryGetProperty("headersMatch", out var matches))
        {
            foreach (var match in matches.EnumerateObject())
            {
                var value = request.Header(match.Name);
                Assert.NotNull(value);
                Assert.Matches(match.Value.GetString()!, value!);
            }
        }

        if (expect.TryGetProperty("headersNotMatch", out var notMatches))
        {
            foreach (var match in notMatches.EnumerateObject())
            {
                var value = request.Header(match.Name);
                if (value is not null)
                {
                    Assert.DoesNotMatch(match.Value.GetString()!, value);
                }
            }
        }

        if (expect.TryGetProperty("queryJson", out var queryJson))
        {
            foreach (var entry in queryJson.EnumerateObject())
            {
                var raw = request.QueryValue(entry.Name);
                Assert.True(raw is not null, $"Expected query parameter '{entry.Name}'.");

                using var actual = JsonDocument.Parse(raw!);
                AssertJsonEquivalent(entry.Value, actual.RootElement, entry.Name);
            }
        }

        foreach (var absent in EnumerateStrings(expect, "queryAbsent"))
        {
            Assert.True(
                request.QueryValue(absent) is null,
                $"Query parameter '{absent}' must be absent, but was '{request.QueryValue(absent)}'.");
        }

        if (expect.TryGetProperty("queryJsonAbsentKeys", out var absentKeys))
        {
            foreach (var entry in absentKeys.EnumerateObject())
            {
                using var actual = JsonDocument.Parse(request.QueryValue(entry.Name)!);
                foreach (var key in entry.Value.EnumerateArray())
                {
                    Assert.False(
                        actual.RootElement.TryGetProperty(key.GetString()!, out _),
                        $"'{entry.Name}' must not contain '{key.GetString()}' — only options the " +
                        "caller explicitly set may be serialized.");
                }
            }
        }

        if (expect.TryGetProperty("bodyText", out var bodyText))
        {
            Assert.Equal(bodyText.GetString(), request.BodyText);
        }

        if (expect.TryGetProperty("query", out var query))
        {
            foreach (var entry in query.EnumerateObject())
            {
                Assert.Equal(entry.Value.GetString(), request.QueryValue(entry.Name));
            }
        }

        if (expect.TryGetProperty("bodyJson", out var bodyJson))
        {
            using var actual = JsonDocument.Parse(request.BodyText);
            AssertJsonEquivalent(bodyJson, actual.RootElement, "body");
        }
    }

    // -------------------------------------------------------------------- print

    private async Task RunPrintCase(JsonElement given, JsonElement expect)
    {
        var handler = new StubHttpMessageHandler();
        EnqueueScripted(handler, given);

        var sleeps = new List<double>();
        using var client = BuildClient(given, handler, delay =>
        {
            sleeps.Add(delay.TotalSeconds);
            return Task.CompletedTask;
        }, defaultMaxRetries: 0);

        if (expect.TryGetProperty("validationError", out var validationError))
        {
            var thrown = await Assert.ThrowsAsync<LabelZoomValidationException>(() => RunPrintOperation(client, given));
            Assert.Equal(validationError.GetProperty("parameter").GetString(), thrown.ParamName);
            Assert.Equal(expect.GetProperty("requestsSent").GetInt32(), handler.Requests.Count);
            return;
        }

        if (expect.TryGetProperty("error", out var expectedError))
        {
            var actual = await Assert.ThrowsAnyAsync<LabelZoomException>(() => RunPrintOperation(client, given));
            AssertError(expectedError, actual);
        }
        else
        {
            var result = await RunPrintOperation(client, given);
            using var actual = JsonDocument.Parse(JsonSerializer.Serialize(result));
            var expectedResult = expect.GetProperty("result").EnumerateObject().Single();
            AssertSubset(expectedResult.Value, actual.RootElement, expectedResult.Name);
        }

        if (expect.TryGetProperty("request", out var request))
        {
            AssertRequest(handler.Requests[0], request);
        }

        if (expect.TryGetProperty("attempts", out var attempts))
        {
            Assert.Equal(attempts.GetInt32(), handler.Requests.Count);
        }

        if (expect.TryGetProperty("sleepsSeconds", out var expectedSleeps))
        {
            Assert.Equal(expectedSleeps.EnumerateArray().Select(e => e.GetDouble()).ToList(), sleeps);
        }

        foreach (var header in EnumerateStrings(expect, "sameHeaderAcrossAttempts"))
        {
            var values = handler.Requests.Select(r => r.Header(header)).ToList();
            Assert.NotNull(values[0]);
            Assert.All(values, value => Assert.Equal(values[0], value));
        }
    }

    /// <summary>Runs the fixture's print call and projects its result into the fixture's naming.</summary>
    private static async Task<object> RunPrintOperation(LabelZoomClient client, JsonElement given)
    {
        switch (given.GetProperty("operation").GetString())
        {
            case "print":
            {
                var builder = client.Print(given.GetProperty("printerId").GetString()!)
                    .From(ParseSource(given.GetProperty("source").GetString()!), given.GetProperty("bodyText").GetString()!);
                if (given.TryGetProperty("options", out var options))
                {
                    ApplyPrintSourceOptions(builder, options);
                }

                if (given.TryGetProperty("copies", out var copies))
                {
                    builder.WithCopies(copies.GetInt32());
                }

                if (given.TryGetProperty("idempotencyKey", out var key))
                {
                    builder.WithIdempotencyKey(key.GetString()!);
                }

                return Project(await builder.SendAsync());
            }

            case "printTemplate":
            {
                var builder = client.Print(given.GetProperty("printerId").GetString()!)
                    .FromTemplate(given.GetProperty("templateId").GetString()!);
                if (given.TryGetProperty("data", out var data))
                {
                    builder.WithData(data.EnumerateArray()
                        .Select(record => record.ValueKind == JsonValueKind.String
                            ? (object?)record.GetString()
                            : record.Clone())
                        .ToArray());
                }

                if (given.TryGetProperty("validate", out var validate))
                {
                    builder.WithValidate(validate.GetBoolean());
                }

                if (given.TryGetProperty("copies", out var copies))
                {
                    builder.WithCopies(copies.GetInt32());
                }

                if (given.TryGetProperty("idempotencyKey", out var key))
                {
                    builder.WithIdempotencyKey(key.GetString()!);
                }

                var batch = await builder.SendAsync();
                return new Dictionary<string, object?>
                {
                    ["jobs"] = batch.Jobs.Select(Project).ToList(),
                    ["remaining"] = batch.Remaining,
                    ["message"] = batch.Message,
                };
            }

            case "getJob":
            {
                var job = await client.Jobs.GetAsync(given.GetProperty("jobId").GetString()!);
                return new Dictionary<string, object?>
                {
                    ["id"] = job.Id,
                    ["printerId"] = job.PrinterId,
                    ["agentId"] = job.AgentId,
                    ["sourceFormat"] = job.SourceFormat,
                    ["copies"] = job.Copies,
                    ["status"] = job.Status,
                    ["error"] = job.Error,
                    ["retryCount"] = job.RetryCount,
                    ["createdDate"] = job.CreatedDate,
                    ["dispatchedDate"] = job.DispatchedDate,
                    ["completedDate"] = job.CompletedDate,
                    ["retriedDate"] = job.RetriedDate,
                };
            }

            case "getQuota":
            {
                var quota = await client.GetPrintQuotaAsync();
                return new Dictionary<string, object?>
                {
                    ["plan"] = quota.Plan,
                    ["agents"] = new Dictionary<string, object?>
                    {
                        ["used"] = quota.Agents.Used, ["pending"] = quota.Agents.Pending,
                        ["limit"] = quota.Agents.Limit, ["remaining"] = quota.Agents.Remaining,
                    },
                    ["printers"] = new Dictionary<string, object?>
                    {
                        ["used"] = quota.Printers.Used, ["limit"] = quota.Printers.Limit,
                        ["remaining"] = quota.Printers.Remaining,
                    },
                    ["jobsToday"] = new Dictionary<string, object?>
                    {
                        ["used"] = quota.JobsToday.Used, ["limit"] = quota.JobsToday.Limit,
                        ["remaining"] = quota.JobsToday.Remaining, ["resetAt"] = quota.JobsToday.ResetAt,
                    },
                    ["limits"] = new Dictionary<string, object?>
                    {
                        ["maxPayloadBytes"] = quota.Limits.MaxPayloadBytes,
                        ["maxStoredPayloadBytes"] = quota.Limits.MaxStoredPayloadBytes,
                        ["maxBatchSize"] = quota.Limits.MaxBatchSize,
                    },
                };
            }

            case var operation:
                throw new InvalidOperationException($"Unknown print operation '{operation}'.");
        }
    }

    private static Dictionary<string, object?> Project(PrintJob job) => new Dictionary<string, object?>
    {
        ["jobId"] = job.JobId,
        ["status"] = job.Status,
        ["copies"] = job.Copies,
        ["idempotent"] = job.Idempotent,
    };

    private static void ApplyPrintSourceOptions(DynamicPrintBuilder builder, JsonElement options)
    {
        foreach (var option in options.EnumerateObject())
        {
            switch (option.Name)
            {
                case "label":
                    builder.WithLabelSize(
                        option.Value.GetProperty("width").GetSingle(),
                        option.Value.GetProperty("height").GetSingle());
                    break;
                case "pdf":
                    if (option.Value.TryGetProperty("pageNumber", out var page))
                    {
                        builder.WithPdfPage(page.GetInt32());
                    }

                    break;
                case "split":
                    builder.WithSplit(split =>
                    {
                        if (option.Value.TryGetProperty("rows", out var rows))
                        {
                            split.Rows(rows.GetInt32());
                        }

                        if (option.Value.TryGetProperty("columns", out var columns))
                        {
                            split.Columns(columns.GetInt32());
                        }
                    });
                    break;
                default:
                    throw new InvalidOperationException(
                        $"The print runner has no mapping for source option '{option.Name}'. Add one.");
            }
        }
    }

    /// <summary>
    /// Asserts every member the fixture gives, and only those. Timestamps compare as instants.
    /// </summary>
    private static void AssertSubset(JsonElement expected, JsonElement actual, string path)
    {
        switch (expected.ValueKind)
        {
            case JsonValueKind.Object:
                Assert.True(actual.ValueKind == JsonValueKind.Object, $"{path}: expected an object.");
                foreach (var property in expected.EnumerateObject())
                {
                    Assert.True(actual.TryGetProperty(property.Name, out var value), $"{path}.{property.Name} is missing.");
                    AssertSubset(property.Value, value, $"{path}.{property.Name}");
                }

                break;
            case JsonValueKind.Array:
                var expectedItems = expected.EnumerateArray().ToList();
                var actualItems = actual.EnumerateArray().ToList();
                Assert.Equal(expectedItems.Count, actualItems.Count);
                for (var i = 0; i < expectedItems.Count; i++)
                {
                    AssertSubset(expectedItems[i], actualItems[i], $"{path}[{i}]");
                }

                break;
            case JsonValueKind.Number:
                Assert.Equal(expected.GetDouble(), actual.GetDouble(), 6);
                break;
            case JsonValueKind.String
                when DateTimeOffset.TryParse(expected.GetString(), out var expectedInstant) &&
                     expected.GetString()!.Contains('T'):
                Assert.True(
                    DateTimeOffset.TryParse(actual.GetString(), out var actualInstant),
                    $"{path}: expected a timestamp but got '{actual}'.");
                Assert.Equal(expectedInstant.ToUniversalTime(), actualInstant.ToUniversalTime());
                break;
            default:
                Assert.True(expected.ValueKind == actual.ValueKind, $"{path}: expected {expected} but got {actual}.");
                if (expected.ValueKind == JsonValueKind.String)
                {
                    Assert.Equal(expected.GetString(), actual.GetString());
                }

                break;
        }
    }

    // ----------------------------------------------------------------- response

    private async Task RunResponseCase(JsonElement given, JsonElement expect)
    {
        var handler = new StubHttpMessageHandler();
        EnqueueScripted(handler, given);

        // Response cases queue exactly one response and assert how it maps. Retry is the
        // subject of retry/*, and leaving it on here would consume responses that do not
        // exist for the 429 and 5xx cases.
        using var client = BuildClient(given, handler, defaultMaxRetries: 0);

        var call = client.Convert().FromZpl("^XA^XZ").ToZpl().ExecuteAsync();

        if (expect.TryGetProperty("error", out var expectedError))
        {
            var actual = await Assert.ThrowsAnyAsync<LabelZoomException>(() => call);
            AssertError(expectedError, actual);
            return;
        }

        var result = await call;
        var expectedResult = expect.GetProperty("result");

        if (expectedResult.TryGetProperty("status", out var status))
        {
            Assert.Equal(status.GetInt32(), result.StatusCode);
        }

        if (expectedResult.TryGetProperty("contentType", out var contentType))
        {
            Assert.Equal(contentType.GetString(), result.ContentType);
        }

        if (expectedResult.TryGetProperty("text", out var text))
        {
            Assert.Equal(text.GetString(), result.Text);
        }

        if (expectedResult.TryGetProperty("bytesBase64", out var bytes))
        {
            Assert.Equal(Convert.FromBase64String(bytes.GetString()!), result.Bytes);
        }

        if (expectedResult.TryGetProperty("requestId", out var requestId))
        {
            Assert.Equal(
                requestId.ValueKind == JsonValueKind.Null ? null : requestId.GetString(),
                result.RequestId);
        }
    }

    // -------------------------------------------------------------------- retry

    private async Task RunRetryCase(JsonElement given, JsonElement expect)
    {
        var handler = new StubHttpMessageHandler();
        EnqueueScripted(handler, given);

        var sleeps = new List<double>();
        using var client = BuildClient(given, handler, delay =>
        {
            sleeps.Add(delay.TotalSeconds);
            return Task.CompletedTask;
        });

        var call = client.Convert().FromZpl("^XA^XZ").ToZpl().ExecuteAsync();

        if (expect.TryGetProperty("error", out var expectedError))
        {
            var actual = await Assert.ThrowsAnyAsync<LabelZoomException>(() => call);
            AssertError(expectedError, actual);
        }
        else
        {
            var result = await call;
            if (expect.GetProperty("result").TryGetProperty("text", out var text))
            {
                Assert.Equal(text.GetString(), result.Text);
            }
        }

        Assert.Equal(expect.GetProperty("attempts").GetInt32(), handler.Requests.Count);

        var expectedSleeps = expect.GetProperty("sleepsSeconds")
            .EnumerateArray().Select(e => e.GetDouble()).ToList();
        Assert.Equal(expectedSleeps, sleeps);
    }

    // --------------------------------------------------------------- validation

    private Task RunValidationCase(JsonElement given, JsonElement expect)
    {
        var handler = new StubHttpMessageHandler();
        using var client = BuildClient(given, handler);

        var thrown = Assert.Throws<LabelZoomValidationException>(() =>
        {
            var builder = BuildRequest(client, given);
            // ExecuteAsync is never reached: every validation case must fail while building.
            _ = builder;
        });

        var parameter = expect.GetProperty("validationError").GetProperty("parameter").GetString();
        Assert.Equal(parameter, thrown.ParamName);

        // No queued responses were consumed, and none should have been requested.
        Assert.Equal(expect.GetProperty("requestsSent").GetInt32(), handler.Requests.Count);
        return Task.CompletedTask;
    }

    // ---------------------------------------------------------------- typecheck

    /// <summary>
    /// The runtime stand-in for "this snippet must not compile".
    /// </summary>
    /// <remarks>
    /// A test cannot assert a compile error about itself, so it asserts the property that makes the
    /// compile error inevitable: <see cref="TargetFormat"/> has no member for the source-only
    /// formats, and it is a distinct type from <see cref="SourceFormat"/>, so neither
    /// <c>To(URL)</c> nor <c>To(SourceFormat.Pdf)</c> can be written at all.
    /// </remarks>
    private Task RunTypecheckCase(JsonElement given)
    {
        var snippet = given.GetProperty("snippet").GetString()!;
        var targetNames = Enum.GetNames(typeof(TargetFormat));

        foreach (var sourceOnly in new[] { "URL", "JPG" })
        {
            if (snippet.Contains($"to({sourceOnly})", StringComparison.OrdinalIgnoreCase))
            {
                Assert.DoesNotContain(
                    targetNames,
                    name => string.Equals(name, sourceOnly, StringComparison.OrdinalIgnoreCase));
            }
        }

        if (snippet.Contains("SourceFormat.", StringComparison.Ordinal))
        {
            Assert.NotEqual(typeof(SourceFormat), typeof(TargetFormat));
            Assert.False(
                typeof(TargetFormat).IsAssignableFrom(typeof(SourceFormat)),
                "SourceFormat must not be usable where a TargetFormat is expected.");
        }

        return Task.CompletedTask;
    }

    // ------------------------------------------------------------------ helpers

    private static LabelZoomClient BuildClient(
        JsonElement given,
        StubHttpMessageHandler handler,
        Func<TimeSpan, Task>? onSleep = null,
        int? defaultMaxRetries = null)
    {
        var options = new LabelZoomClientOptions
        {
            HttpMessageHandler = handler,
            // Deterministic backoff: the fixtures assert exact sleep durations.
            UseJitter = false,
            SleepAsync = onSleep is null
                ? (_, _) => Task.CompletedTask
                : (delay, _) => onSleep(delay),
        };

        if (given.TryGetProperty("client", out var clientOptions))
        {
            if (clientOptions.TryGetProperty("apiKey", out var apiKey))
            {
                options.ApiKey = apiKey.ValueKind == JsonValueKind.Null ? null : apiKey.GetString();
            }

            if (clientOptions.TryGetProperty("baseUrl", out var baseUrl))
            {
                options.BaseUrl = baseUrl.GetString()!;
            }
        }
        else
        {
            // Cases that do not configure a client (retry, validation) still must not pick up a
            // developer's real key from the environment.
            options.ApiKey = string.Empty;
        }

        if (given.TryGetProperty("maxRetries", out var maxRetries))
        {
            options.MaxRetries = maxRetries.GetInt32();
        }
        else if (defaultMaxRetries.HasValue)
        {
            options.MaxRetries = defaultMaxRetries.Value;
        }

        return new LabelZoomClient(options);
    }

    /// <summary>Translates the fixture's wire-shaped options into fluent SDK calls.</summary>
    /// <remarks>
    /// The fixtures are wire-shaped, so one fixture object can mix source and target options: the
    /// <c>zpl</c> group holds both <c>commandsToIgnore</c> (source) and <c>imageCompression</c>
    /// (target). The runner applies the source options before <c>To</c> and the target options
    /// after it, which is the order the staged builders require.
    /// </remarks>
    private static DynamicTargetBuilder BuildRequest(LabelZoomClient client, JsonElement given)
    {
        var source = ParseSource(given.GetProperty("source").GetString()!);
        var target = ParseTarget(given.GetProperty("target").GetString()!);
        var body = given.GetProperty("bodyText").GetString()!;

        var isBase64Text = given.TryGetProperty("sourceEncoding", out var encoding) &&
                           encoding.GetString() == "base64text";

        var sourceStage = isBase64Text
            ? client.Convert().FromBase64Text(source, body)
            : client.Convert().From(source, body);

        var hasOptions = given.TryGetProperty("options", out var options);

        if (hasOptions)
        {
            ApplySourceOptions(sourceStage, options);
        }

        var targetStage = sourceStage.To(target);

        if (hasOptions)
        {
            ApplyTargetOptions(targetStage, options);
        }

        return targetStage;
    }

    private static readonly HashSet<string> TargetOptionNames = new HashSet<string>
    {
        "targetDpi", "rotation", "watermark", "dialect", "layout",
    };

    private static void ApplySourceOptions(DynamicSourceBuilder builder, JsonElement options)
    {
        foreach (var option in options.EnumerateObject())
        {
            switch (option.Name)
            {
                case "label":
                    builder.WithLabelSize(
                        option.Value.GetProperty("width").GetSingle(),
                        option.Value.GetProperty("height").GetSingle());
                    break;
                case "pdf":
                    if (option.Value.TryGetProperty("conversionMode", out var mode))
                    {
                        builder.WithPdfConversionMode(ParseEnum<PdfConversionMode>(mode.GetString()!));
                    }

                    if (option.Value.TryGetProperty("pageNumber", out var page))
                    {
                        builder.WithPdfPage(page.GetInt32());
                    }

                    break;
                case "zpl":
                    if (option.Value.TryGetProperty("commandsToIgnore", out var commands))
                    {
                        builder.WithZplCommandsToIgnore(
                            commands.EnumerateArray().Select(e => e.GetString()!).ToArray());
                    }

                    break;
                case "split":
                    builder.WithSplit(s =>
                    {
                        if (option.Value.TryGetProperty("rows", out var rows))
                        {
                            s.Rows(rows.GetInt32());
                        }

                        if (option.Value.TryGetProperty("columns", out var columns))
                        {
                            s.Columns(columns.GetInt32());
                        }

                        if (option.Value.TryGetProperty("width", out var width))
                        {
                            s.LabelSize(width.GetSingle(), option.Value.GetProperty("height").GetSingle());
                        }

                        if (option.Value.TryGetProperty("left", out var left))
                        {
                            s.Margins(left.GetSingle(), option.Value.GetProperty("top").GetSingle());
                        }

                        if (option.Value.TryGetProperty("columnGap", out var columnGap))
                        {
                            s.Gaps(columnGap.GetSingle(), option.Value.GetProperty("rowGap").GetSingle());
                        }

                        if (option.Value.TryGetProperty("skipBlank", out var skipBlank))
                        {
                            s.SkipBlank(skipBlank.GetBoolean());
                        }
                    });
                    break;
                case "labelIndex":
                    builder.WithLabelIndex(option.Value.GetInt32());
                    break;
                case "sourceDpi":
                    builder.WithDpi(option.Value.GetInt32());
                    break;
                // How an image or PDF is rasterized: a source option, set as one group.
                case "scaling":
                    builder.WithRaster(r => r.Scaling(option.Value.GetSingle()));
                    break;
                case "colorMode":
                    builder.WithRaster(r => r.ColorMode(ParseEnum<ColorMode>(option.Value.GetString()!)));
                    break;
                case "darkness":
                    builder.WithRaster(r => r.Darkness(option.Value.GetInt32()));
                    break;
                case "position":
                    builder.WithRaster(r => r.Position(
                        option.Value.GetProperty("x").GetInt32(),
                        option.Value.GetProperty("y").GetInt32()));
                    break;
                case "data":
                    // A bare object means one label, so it is wrapped rather than rejected.
                    builder.WithData(option.Value.ValueKind == JsonValueKind.Array
                        ? option.Value.EnumerateArray().Select(e => (object?)e.Clone()).ToArray()
                        : new object?[] { option.Value.Clone() });
                    break;
                default:
                    if (!TargetOptionNames.Contains(option.Name))
                    {
                        throw new InvalidOperationException(
                            $"Fixture sets option '{option.Name}', which the .NET runner does not map. " +
                            "Add it to ApplySourceOptions or ApplyTargetOptions rather than skipping the case.");
                    }

                    break;
            }
        }
    }

    private static void ApplyTargetOptions(DynamicTargetBuilder builder, JsonElement options)
    {
        foreach (var option in options.EnumerateObject())
        {
            switch (option.Name)
            {
                case "targetDpi":
                    builder.WithDpi(option.Value.GetInt32());
                    break;
                case "rotation":
                    builder.WithRotation(option.Value.GetInt32());
                    break;
                case "watermark":
                    builder.WithWatermark(option.Value.GetBoolean());
                    break;
                case "dialect":
                    builder.WithDialect(option.Value.GetString()!);
                    break;
                case "zpl":
                    if (option.Value.TryGetProperty("imageCompression", out var compression))
                    {
                        builder.WithZplImageCompression(
                            ParseEnum<ZplImageCompression>(compression.GetString()!));
                    }

                    if (option.Value.TryGetProperty("collapseCopies", out var collapse))
                    {
                        builder.WithZplCollapseCopies(collapse.GetBoolean());
                    }

                    break;
                case "layout":
                    builder.WithLayout(l =>
                    {
                        if (option.Value.TryGetProperty("across", out var across))
                        {
                            l.Across(across.GetInt32());
                        }

                        if (option.Value.TryGetProperty("gap", out var gap))
                        {
                            l.Gap(gap.GetSingle());
                        }

                        if (option.Value.TryGetProperty("fill", out var fill))
                        {
                            l.Fill(ParseEnum<LayoutFill>(fill.GetString()!));
                        }
                    });
                    break;
                default:
                    // A source option, already applied before To(). ApplySourceOptions rejects
                    // a name that is neither.
                    break;
            }
        }
    }

    private static void EnqueueScripted(StubHttpMessageHandler handler, JsonElement given)
    {
        if (given.TryGetProperty("responses", out var responses))
        {
            foreach (var response in responses.EnumerateArray())
            {
                EnqueueOne(handler, response);
            }
        }
        else
        {
            EnqueueOne(handler, given);
        }
    }

    private static void EnqueueOne(StubHttpMessageHandler handler, JsonElement response)
    {
        if (response.TryGetProperty("transportError", out _))
        {
            handler.EnqueueTransportError();
            return;
        }

        var status = (HttpStatusCode)response.GetProperty("status").GetInt32();

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? contentType = null;
        if (response.TryGetProperty("headers", out var headerElement))
        {
            foreach (var header in headerElement.EnumerateObject())
            {
                if (string.Equals(header.Name, "content-type", StringComparison.OrdinalIgnoreCase))
                {
                    contentType = header.Value.GetString();
                }
                else
                {
                    headers[header.Name] = header.Value.GetString()!;
                }
            }
        }

        if (response.TryGetProperty("bodyBase64", out var base64))
        {
            handler.EnqueueBytes(
                status, Convert.FromBase64String(base64.GetString()!), contentType ?? "application/octet-stream", headers);
            return;
        }

        var body = response.TryGetProperty("bodyTextRepeat", out var repeat)
            ? new string(repeat[0].GetString()![0], repeat[1].GetInt32())
            : response.GetProperty("bodyText").GetString()!;

        handler.Enqueue(status, body, contentType, headers);
    }

    private static void AssertError(JsonElement expected, LabelZoomException actual)
    {
        if (expected.TryGetProperty("kind", out var kind))
        {
            Assert.Equal(ExceptionTypeFor(kind.GetString()!), actual.GetType());
        }

        if (expected.TryGetProperty("status", out var status))
        {
            Assert.Equal(status.GetInt32(), (int)actual.StatusCode);
        }

        if (expected.TryGetProperty("message", out var message))
        {
            Assert.Equal(message.GetString(), actual.Message);
        }

        if (expected.TryGetProperty("messageNonEmpty", out _))
        {
            Assert.False(string.IsNullOrWhiteSpace(actual.Message));
        }

        if (expected.TryGetProperty("messageMaxLength", out var maxLength))
        {
            Assert.True(
                actual.Message.Length <= maxLength.GetInt32(),
                $"Message is {actual.Message.Length} chars; the cap is {maxLength.GetInt32()}.");
        }

        if (expected.TryGetProperty("rawBodyLength", out var rawLength))
        {
            Assert.Equal(rawLength.GetInt32(), actual.RawBody?.Length);
        }

        if (expected.TryGetProperty("rawBodyPresent", out var rawPresent) && rawPresent.GetBoolean())
        {
            Assert.False(string.IsNullOrEmpty(actual.RawBody));
        }

        if (expected.TryGetProperty("requestId", out var requestId))
        {
            Assert.Equal(requestId.GetString(), actual.RequestId);
        }

        if (expected.TryGetProperty("isPaidFeature", out var paid))
        {
            var forbidden = Assert.IsType<LabelZoomForbiddenException>(actual);
            Assert.Equal(paid.GetBoolean(), forbidden.IsPaidFeature);
        }

        if (expected.TryGetProperty("code", out var code))
        {
            Assert.Equal(code.ValueKind == JsonValueKind.Null ? null : code.GetString(), actual.Code);
        }

        if (expected.TryGetProperty("retryAfterSeconds", out var retryAfter))
        {
            var limited = Assert.IsAssignableFrom<LabelZoomRateLimitedException>(actual);
            Assert.Equal(retryAfter.GetInt32(), limited.RetryAfterSeconds);
        }
    }

    private static Type ExceptionTypeFor(string kind) => kind switch
    {
        "BadRequest" => typeof(LabelZoomBadRequestException),
        "Unauthorized" => typeof(LabelZoomUnauthorizedException),
        "Forbidden" => typeof(LabelZoomForbiddenException),
        "NotFound" => typeof(LabelZoomNotFoundException),
        "PayloadTooLarge" => typeof(LabelZoomPayloadTooLargeException),
        "RateLimited" => typeof(LabelZoomRateLimitedException),
        "ServerError" => typeof(LabelZoomServerException),
        "Unprocessable" => typeof(LabelZoomUnprocessableException),
        "PrintQuotaExceeded" => typeof(LabelZoomPrintQuotaExceededException),
        _ => throw new InvalidOperationException($"Unknown error kind '{kind}'."),
    };

    /// <summary>
    /// Structural JSON comparison.
    /// </summary>
    /// <remarks>
    /// Property order is not guaranteed and differs across the seven SDK languages, and numbers
    /// round-trip differently (4 vs 4.0), so both are normalized rather than compared textually.
    /// </remarks>
    private static void AssertJsonEquivalent(JsonElement expected, JsonElement actual, string path)
    {
        switch (expected.ValueKind)
        {
            case JsonValueKind.Object:
                Assert.True(
                    actual.ValueKind == JsonValueKind.Object,
                    $"{path}: expected an object but got {actual.ValueKind}.");
                foreach (var property in expected.EnumerateObject())
                {
                    Assert.True(
                        actual.TryGetProperty(property.Name, out var actualValue),
                        $"{path}.{property.Name} is missing.");
                    AssertJsonEquivalent(property.Value, actualValue, $"{path}.{property.Name}");
                }

                Assert.Equal(
                    expected.EnumerateObject().Count(),
                    actual.EnumerateObject().Count());
                break;

            case JsonValueKind.Array:
                Assert.True(
                    actual.ValueKind == JsonValueKind.Array,
                    $"{path}: expected an array but got {actual.ValueKind}.");
                var expectedItems = expected.EnumerateArray().ToList();
                var actualItems = actual.EnumerateArray().ToList();
                Assert.Equal(expectedItems.Count, actualItems.Count);
                for (var i = 0; i < expectedItems.Count; i++)
                {
                    AssertJsonEquivalent(expectedItems[i], actualItems[i], $"{path}[{i}]");
                }

                break;

            case JsonValueKind.Number:
                Assert.True(
                    actual.ValueKind == JsonValueKind.Number,
                    $"{path}: expected a number but got {actual.ValueKind}.");
                Assert.Equal(expected.GetDouble(), actual.GetDouble(), 6);
                break;

            default:
                Assert.Equal(expected.ValueKind, actual.ValueKind);
                if (expected.ValueKind == JsonValueKind.String)
                {
                    Assert.Equal(expected.GetString(), actual.GetString());
                }

                break;
        }
    }

    private static IEnumerable<string> EnumerateStrings(JsonElement element, string property) =>
        element.TryGetProperty(property, out var array)
            ? array.EnumerateArray().Select(e => e.GetString()!)
            : Enumerable.Empty<string>();

    private static SourceFormat ParseSource(string token) =>
        Enum.Parse<SourceFormat>(token, ignoreCase: true);

    private static TargetFormat ParseTarget(string token) =>
        Enum.Parse<TargetFormat>(token, ignoreCase: true);

    private static T ParseEnum<T>(string wireToken) where T : struct, Enum =>
        Enum.Parse<T>(wireToken.Replace("_", string.Empty), ignoreCase: true);

    /// <summary>
    /// Applies a fixture's <c>given.env</c> for the duration of a case, then restores it.
    /// </summary>
    private sealed class EnvironmentScope : IDisposable
    {
        private readonly Dictionary<string, string?> _previous = new();

        private EnvironmentScope() { }

        public static EnvironmentScope From(JsonElement given)
        {
            var scope = new EnvironmentScope();

            // Always neutralize the real key so a developer's shell cannot change the outcome.
            scope.Set(LabelZoomClientOptions.ApiKeyEnvironmentVariable, null);

            if (given.TryGetProperty("env", out var env))
            {
                foreach (var variable in env.EnumerateObject())
                {
                    scope.Set(variable.Name, variable.Value.GetString());
                }
            }

            return scope;
        }

        private void Set(string name, string? value)
        {
            if (!_previous.ContainsKey(name))
            {
                _previous[name] = Environment.GetEnvironmentVariable(name);
            }

            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
        {
            foreach (var entry in _previous)
            {
                Environment.SetEnvironmentVariable(entry.Key, entry.Value);
            }
        }
    }
}
