using System.Net;
using System.Text;
using Callu.Infrastructure.Services;
using Callu.Shared.Models.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace Callu.Tests;

public class TracingQueryMappingTests
{
    private static JaegerTracingQueryService ServiceReturning(string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new StubHandler(json, status);
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://trace-backend:16686/") };
        return new JaegerTracingQueryService(client, NullLogger<JaegerTracingQueryService>.Instance);
    }

    private const string TwoSpanTrace = """
        {"data":[{
          "traceID":"abc123",
          "spans":[
            {"traceID":"abc123","spanID":"aaaa","operationName":"GET /api/v1/incidents",
             "startTime":1700000000000000,"duration":5000,"processID":"p1","references":[],"tags":[]},
            {"traceID":"abc123","spanID":"bbbb","operationName":"SELECT incidents",
             "startTime":1700000000001000,"duration":2000,"processID":"p2",
             "references":[{"refType":"CHILD_OF","traceID":"abc123","spanID":"aaaa"}],"tags":[]}
          ],
          "processes":{"p1":{"serviceName":"Callu"},"p2":{"serviceName":"postgres"}}
        }]}
        """;

    [Fact]
    public async Task Microseconds_AreConvertedToMilliseconds_NotPassedThrough()
    {
        var trace = await ServiceReturning(TwoSpanTrace).GetTraceAsync("abc123");

        Assert.NotNull(trace);
        Assert.Equal(5, trace.DurationMs, precision: 3);
        Assert.Equal(5, trace.Spans[0].DurationMs, precision: 3);
        Assert.Equal(2, trace.Spans[1].DurationMs, precision: 3);
    }

    [Fact]
    public async Task ChildSpan_IsOffsetFromTheTraceStart_SoTheWaterfallHasABaseline()
    {
        var trace = await ServiceReturning(TwoSpanTrace).GetTraceAsync("abc123");

        Assert.NotNull(trace);
        Assert.Equal(0, trace.Spans[0].StartOffsetMs, precision: 3);
        Assert.Equal(1, trace.Spans[1].StartOffsetMs, precision: 3);
        Assert.Equal("aaaa", trace.Spans[1].ParentSpanId);
    }

    [Fact]
    public async Task ServiceName_ComesFromTheProcessMap_NotTheSpan()
    {
        var trace = await ServiceReturning(TwoSpanTrace).GetTraceAsync("abc123");

        Assert.NotNull(trace);
        Assert.Equal("Callu", trace.Spans[0].Service);
        Assert.Equal("postgres", trace.Spans[1].Service);
    }

    [Theory]
    [InlineData("""{"key":"otel.status_code","type":"string","value":"ERROR"}""")]
    [InlineData("""{"key":"error","type":"bool","value":true}""")]
    [InlineData("""{"key":"http.response.status_code","type":"int64","value":500}""")]
    public async Task EveryDialectOfFailure_CountsAsAnError(string tagJson)
    {
        var json = """
            {"data":[{"traceID":"aa11","spans":[
              {"traceID":"aa11","spanID":"s1","operationName":"op","startTime":1700000000000000,
               "duration":1000,"processID":"p1","references":[],"tags":[__TAG__]}
            ],"processes":{"p1":{"serviceName":"Callu"}}}]}
            """.Replace("__TAG__", tagJson);

        var trace = await ServiceReturning(json).GetTraceAsync("aa11");

        Assert.NotNull(trace);
        Assert.True(trace.Spans[0].HasError);
    }

    [Fact]
    public async Task A2xxStatus_IsNotAnError()
    {
        var json = """
            {"data":[{"traceID":"aa11","spans":[
              {"traceID":"aa11","spanID":"s1","operationName":"op","startTime":1700000000000000,
               "duration":1000,"processID":"p1","references":[],
               "tags":[{"key":"http.response.status_code","type":"int64","value":204}]}
            ],"processes":{"p1":{"serviceName":"Callu"}}}]}
            """;

        var trace = await ServiceReturning(json).GetTraceAsync("aa11");

        Assert.NotNull(trace);
        Assert.False(trace.Spans[0].HasError);
    }

    [Fact]
    public async Task Search_ReportsTheRootOperation_EvenWhenTheRootIsNotFirstInTheArray()
    {
        var json = """
            {"data":[{"traceID":"aa11","spans":[
              {"traceID":"aa11","spanID":"child","operationName":"SELECT","startTime":1700000000001000,
               "duration":1000,"processID":"p1","references":[{"refType":"CHILD_OF","traceID":"aa11","spanID":"root"}],"tags":[]},
              {"traceID":"aa11","spanID":"root","operationName":"POST /api/v1/incidents","startTime":1700000000000000,
               "duration":4000,"processID":"p1","references":[],"tags":[]}
            ],"processes":{"p1":{"serviceName":"Callu"}}}]}
            """;

        var results = await ServiceReturning(json).SearchAsync(new TraceSearchRequest { Service = "Callu" });

        var trace = Assert.Single(results);
        Assert.Equal("POST /api/v1/incidents", trace.RootOperation);
        Assert.Equal(2, trace.SpanCount);
        Assert.Equal(0, trace.ErrorCount);
    }

    [Fact]
    public async Task OnlyErrors_DropsTracesThatHaveNone()
    {
        var results = await ServiceReturning(TwoSpanTrace)
            .SearchAsync(new TraceSearchRequest { Service = "Callu", OnlyErrors = true });

        Assert.Empty(results);
    }

    private const string FourTraces = """
        {"data":[
          {"traceID":"a1","spans":[{"traceID":"a1","spanID":"s1","operationName":"calludb",
            "startTime":1700000000000000,"duration":1000,"processID":"p1","references":[],"tags":[]}],
           "processes":{"p1":{"serviceName":"Callu"}}},
          {"traceID":"a2","spans":[{"traceID":"a2","spanID":"s1","operationName":"calludb",
            "startTime":1700000001000000,"duration":3000,"processID":"p1","references":[],"tags":[]}],
           "processes":{"p1":{"serviceName":"Callu"}}},
          {"traceID":"a3","spans":[{"traceID":"a3","spanID":"s1","operationName":"calludb",
            "startTime":1700000002000000,"duration":5000,"processID":"p1","references":[],"tags":[]}],
           "processes":{"p1":{"serviceName":"Callu"}}},
          {"traceID":"a4","spans":[{"traceID":"a4","spanID":"s1","operationName":"execute EscalationProcessingJob",
            "startTime":1700000003000000,"duration":10000,"processID":"p1","references":[],
            "tags":[{"key":"otel.status_code","type":"string","value":"ERROR"}]}],
           "processes":{"p1":{"serviceName":"Callu"}}}
        ]}
        """;

    [Fact]
    public async Task Overview_CollapsesTracesByRootOperation()
    {
        var overview = await ServiceReturning(FourTraces)
            .GetOverviewAsync(new TraceSearchRequest { Service = "Callu" });

        Assert.Equal(4, overview.SampleSize);
        Assert.Equal(2, overview.Operations.Count);

        var db = overview.Operations.Single(op => op.Operation == "calludb");
        Assert.Equal(3, db.TraceCount);
        Assert.Equal(3, db.AvgDurationMs, precision: 3);
        Assert.Equal(5, db.MaxDurationMs, precision: 3);
        Assert.Equal(0, db.ErrorCount);
    }

    [Fact]
    public async Task Overview_PutsFailingOperationsFirst_EvenWhenTheyAreTheRarest()
    {
        var overview = await ServiceReturning(FourTraces)
            .GetOverviewAsync(new TraceSearchRequest { Service = "Callu" });

        Assert.Equal("execute EscalationProcessingJob", overview.Operations[0].Operation);
        Assert.Equal(1, overview.Operations[0].ErrorCount);
        Assert.Equal(1, overview.TotalErrors);
    }

    [Fact]
    public async Task Overview_ReportsTheSlowestTrace_AcrossEveryOperation()
    {
        var overview = await ServiceReturning(FourTraces)
            .GetOverviewAsync(new TraceSearchRequest { Service = "Callu" });

        Assert.Equal(10, overview.MaxDurationMs, precision: 3);
    }

    [Fact]
    public async Task Overview_OnAnEmptyWindow_IsZeroedRatherThanThrowing()
    {
        var overview = await ServiceReturning("""{"data":[]}""")
            .GetOverviewAsync(new TraceSearchRequest { Service = "Callu" });

        Assert.Equal(0, overview.SampleSize);
        Assert.Empty(overview.Operations);
        Assert.Equal(0, overview.MaxDurationMs);
    }

    [Fact]
    public async Task NoEndpointConfigured_ReportsUnavailable_RatherThanThrowing()
    {
        using var client = new HttpClient(new StubHandler("{}", HttpStatusCode.OK));
        var service = new JaegerTracingQueryService(client, NullLogger<JaegerTracingQueryService>.Instance);

        var status = await service.GetStatusAsync();

        Assert.False(status.Available);
        Assert.Equal("no-endpoint", status.Reason);
        Assert.Empty(await service.SearchAsync(new TraceSearchRequest { Service = "Callu" }));
        Assert.Null(await service.GetTraceAsync("abc123"));
    }

    [Fact]
    public async Task AnUnreachableBackend_IsReportedAsUnavailable_NotAsAFault()
    {
        using var client = new HttpClient(new ThrowingHandler()) { BaseAddress = new Uri("http://trace-backend:16686/") };
        var service = new JaegerTracingQueryService(client, NullLogger<JaegerTracingQueryService>.Instance);

        var status = await service.GetStatusAsync();

        Assert.False(status.Available);
        Assert.Equal("unreachable", status.Reason);
    }

    [Fact]
    public async Task ANonHexTraceId_IsRejectedWithoutCallingTheBackend()
    {
        var handler = new StubHandler(TwoSpanTrace, HttpStatusCode.OK);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://trace-backend:16686/") };
        var service = new JaegerTracingQueryService(client, NullLogger<JaegerTracingQueryService>.Instance);

        Assert.Null(await service.GetTraceAsync("../../etc/passwd"));
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task AnUnknownTraceId_Returns404AsNull_NotAnException()
    {
        var trace = await ServiceReturning("""{"data":[]}""", HttpStatusCode.NotFound).GetTraceAsync("abc123");

        Assert.Null(trace);
    }

    private sealed class StubHandler(string json, HttpStatusCode status) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("connection refused");
    }
}
