// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Net.Sockets;
using System.Text;
using uk.osric.asp.Web.Metrics;

[TestFixture]
public sealed class PrometheusMetricsSourceTests {

    private static readonly DateTimeOffset From = DateTimeOffset.FromUnixTimeSeconds(1700000000);
    private static readonly DateTimeOffset To = From.AddMinutes(5);
    private static readonly TimeSpan Step = TimeSpan.FromSeconds(60);

    [Test]
    public async Task ValuesAreParsedAsInvariantStrings() {
        QueryResult result = await QueryAsync("""
            {"status":"success","data":{"resultType":"matrix","result":[
              {"metric":{"outcome":"ok"},"values":[[1700000000,"0.5"],[1700000060,"1.25"]]}
            ]}}
            """);

        MetricsMatrix matrix = (MetricsMatrix)result;
        Assert.That(matrix.Series, Has.Count.EqualTo(1));
        Assert.That(matrix.Series[0].Label, Is.EqualTo("ok"));
        Assert.That(matrix.Series[0].Points, Has.Count.EqualTo(2));
        Assert.That(matrix.Series[0].Points[0].At, Is.EqualTo(From));
        Assert.That(matrix.Series[0].Points[1].Value, Is.EqualTo(1.25));
    }

    [Test]
    public async Task NanAndInfinitiesBecomeGaps() {
        QueryResult result = await QueryAsync("""
            {"status":"success","data":{"resultType":"matrix","result":[
              {"metric":{},"values":[[1700000000,"NaN"],[1700000060,"+Inf"],[1700000120,"-Inf"]]}
            ]}}
            """);

        MetricsMatrix matrix = (MetricsMatrix)result;
        Assert.That(matrix.Series[0].Points.Select(point => point.Value), Is.All.Null);
    }

    [Test]
    public async Task AnUnlabelledSeriesHasNoLabel() {
        QueryResult result = await QueryAsync("""
            {"status":"success","data":{"resultType":"matrix","result":[
              {"metric":{},"values":[[1700000000,"1"]]}
            ]}}
            """);

        Assert.That(((MetricsMatrix)result).Series[0].Label, Is.Empty);
    }

    [Test]
    public async Task APagePathLabelArrivesUnderItsPrometheusName() {
        QueryResult result = await QueryAsync("""
            {"status":"success","data":{"resultType":"matrix","result":[
              {"metric":{"page_path":"/hue/ws"},"values":[[1700000000,"1"]]}
            ]}}
            """);

        Assert.That(((MetricsMatrix)result).Series[0].Label, Is.EqualTo("/hue/ws"));
    }

    [Test]
    public async Task AStatusCodeLabelArrivesUnderItsPrometheusName() {
        QueryResult result = await QueryAsync("""
            {"status":"success","data":{"resultType":"matrix","result":[
              {"metric":{"http_response_status_code":"404"},"values":[[1700000000,"1"]]}
            ]}}
            """);

        Assert.That(((MetricsMatrix)result).Series[0].Label, Is.EqualTo("404"));
    }

    [Test]
    public async Task AReasonLabelArrivesUnderItsPrometheusName() {
        QueryResult result = await QueryAsync("""
            {"status":"success","data":{"resultType":"matrix","result":[
              {"metric":{"reason":"first-use"},"values":[[1700000000,"1"]]}
            ]}}
            """);

        Assert.That(((MetricsMatrix)result).Series[0].Label, Is.EqualTo("first-use"));
    }

    [Test]
    public async Task AnEmptyResultIsAnEmptyMatrix() {
        QueryResult result = await QueryAsync("""
            {"status":"success","data":{"resultType":"matrix","result":[]}}
            """);

        Assert.That(((MetricsMatrix)result).Series, Is.Empty);
    }

    [Test]
    public async Task AnErrorPayloadBecomesUnavailable() {
        QueryResult result = await QueryAsync("""
            {"status":"error","errorType":"bad_data","error":"parse error at char 3"}
            """);

        Assert.That(result, Is.InstanceOf<MetricsUnavailable>());
        Assert.That(((MetricsUnavailable)result).Reason, Is.EqualTo("parse error at char 3"));
    }

    [Test]
    public async Task ANonSuccessStatusBecomesUnavailable() {
        QueryResult result = await QueryAsync("{}", HttpStatusCode.ServiceUnavailable);

        Assert.That(((MetricsUnavailable)result).Reason, Does.Contain("503"));
    }

    [Test]
    public async Task ABadQuerySurfacesPrometheusOwnMessage() {
        QueryResult result = await QueryAsync(
            """{"status":"error","errorType":"bad_data","error":"unknown function with name \"nope\""}""",
            HttpStatusCode.BadRequest);

        Assert.That(((MetricsUnavailable)result).Reason, Is.EqualTo("unknown function with name \"nope\""));
    }

    [Test]
    public async Task TheQueryIsSentWithTheRangeAndStep() {
        StubHttpMessageHandler handler = new(_ => Json("""
            {"status":"success","data":{"resultType":"matrix","result":[]}}
            """));
        using HttpClient http = Client(handler);

        await new PrometheusSource(http, NullLogger<PrometheusSource>.Instance).QueryAsync("sum(rate(x[5m]))", From, To, Step, CancellationToken.None);

        string uri = handler.Requests.Single().ToString();
        Assert.That(uri, Does.Contain("query=sum%28rate%28x%5B5m%5D%29%29"));
        Assert.That(uri, Does.Contain("start=1700000000"));
        Assert.That(uri, Does.Contain("end=1700000300"));
        Assert.That(uri, Does.Contain("step=60"));
    }

    [Test]
    public async Task ATimeoutNamesTheTarget() {
        StubHttpMessageHandler handler = new(_ => throw new TaskCanceledException());
        using HttpClient http = Client(handler);
        PrometheusSource source = new(http, NullLogger<PrometheusSource>.Instance, TimeSpan.FromMilliseconds(20));

        QueryResult result = await source.QueryAsync("up", From, To, Step, CancellationToken.None);

        Assert.That(((MetricsUnavailable)result).Reason, Does.Contain("prometheus:9090"));
        Assert.That(((MetricsUnavailable)result).Reason, Does.Contain("did not answer"));
    }

    [Test]
    public async Task AMissingNameIsReportedAsNotResolving() {
        QueryResult result = await FailingAsync(new SocketException((int)SocketError.HostNotFound));

        Assert.That(((MetricsUnavailable)result).Reason, Is.EqualTo("Prometheus at prometheus:9090 does not resolve."));
    }

    [Test]
    public async Task ARefusedConnectionIsReportedAsUnreachable() {
        QueryResult result = await FailingAsync(new SocketException((int)SocketError.ConnectionRefused));

        Assert.That(((MetricsUnavailable)result).Reason, Is.EqualTo("Prometheus at prometheus:9090 is unreachable."));
    }

    private static async Task<QueryResult> FailingAsync(Exception inner) {
        using HttpClient http = Client(new StubHttpMessageHandler(_ => throw new HttpRequestException("boom", inner)));

        return await new PrometheusSource(http, NullLogger<PrometheusSource>.Instance)
            .QueryAsync("up", From, To, Step, CancellationToken.None);
    }

    private static async Task<QueryResult> QueryAsync(string body, HttpStatusCode status = HttpStatusCode.OK) {
        using HttpClient http = Client(new StubHttpMessageHandler(_ => Json(body, status)));

        return await new PrometheusSource(http, NullLogger<PrometheusSource>.Instance)
            .QueryAsync("up", From, To, Step, CancellationToken.None);
    }

    private static HttpClient Client(StubHttpMessageHandler handler) =>
        new(handler) { BaseAddress = new Uri("http://prometheus:9090/") };

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
