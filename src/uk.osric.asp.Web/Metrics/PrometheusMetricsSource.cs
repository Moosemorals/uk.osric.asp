// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Metrics;

using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using uk.osric.asp.Core;

/// <summary>Talks to Prometheus' <c>query_range</c> API.</summary>
/// <remarks>
/// The response is parsed with small DTOs and a hand-written sample reader, because the values array
/// mixes a number and a <em>string</em>, and the string is where <c>NaN</c> and the infinities hide.
/// </remarks>
internal sealed class PrometheusMetricsSource : IMetricsSource {

    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(2);
    private static readonly string[] LabelKeys = ["page.path", "outcome", "result", "le"];

    private readonly HttpClient _http;

    /// <summary>Creates a source over a client whose base address is the Prometheus origin.</summary>
    /// <param name="http">The client to send queries with.</param>
    public PrometheusMetricsSource(HttpClient http) {
        ArgumentNullException.ThrowIfNull(http);
        _http = http;
    }

    /// <inheritdoc/>
    public async Task<MetricsQueryResult> QueryAsync(
        string query,
        DateTimeOffset from,
        DateTimeOffset to,
        TimeSpan step,
        CancellationToken cancellationToken) {

        string url = string.Create(
            CultureInfo.InvariantCulture,
            $"api/v1/query_range?query={Uri.EscapeDataString(query)}&start={from.ToUnixTimeSeconds()}&end={to.ToUnixTimeSeconds()}&step={(long)step.TotalSeconds}");

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(QueryTimeout);

        try {
            using HttpRequestMessage request = new(HttpMethod.Get, url);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using HttpResponseMessage response = await _http.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode) {
                return await UnavailableAsync(response, timeout.Token);
            }

            PrometheusResponse? payload = await response.Content.ReadFromJsonAsync<PrometheusResponse>(timeout.Token);
            return ToResult(payload);
        } catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            return new MetricsUnavailable("Prometheus did not answer within 2 seconds.");
        } catch (HttpRequestException) {
            return new MetricsUnavailable("Prometheus is unreachable.");
        } catch (JsonException) {
            return new MetricsUnavailable("Prometheus returned a response this page cannot read.");
        }
    }

    // A bad query comes back as 400 carrying {status: error, error: "..."}: the message is worth
    // far more than the status code, so read it when the body is the JSON we expect.
    private static async Task<MetricsQueryResult> UnavailableAsync(HttpResponseMessage response, CancellationToken cancellationToken) {
        try {
            PrometheusResponse? payload = await response.Content.ReadFromJsonAsync<PrometheusResponse>(cancellationToken);
            return new MetricsUnavailable(payload?.Error ?? $"Prometheus answered {(int)response.StatusCode}.");
        } catch (JsonException) {
            return new MetricsUnavailable($"Prometheus answered {(int)response.StatusCode}.");
        }
    }

    private static MetricsQueryResult ToResult(PrometheusResponse? payload) {
        if (payload is null) {
            return new MetricsUnavailable("Prometheus returned no payload.");
        }

        if (!string.Equals(payload.Status, "success", StringComparison.Ordinal)) {
            return new MetricsUnavailable(payload.Error ?? $"Prometheus reported {payload.ErrorType ?? "an error"}.");
        }

        List<ChartSeries> series = [];
        foreach (PrometheusResult result in payload.Data?.Result ?? []) {
            series.Add(new ChartSeries(LabelFor(result.Metric), Samples(result.Values)));
        }

        return new MetricsMatrix(series);
    }

    // The label a legend line should read: the ranked keys if present, otherwise whatever Prometheus
    // called the series, so an unexpected query still produces something recognisable.
    private static string LabelFor(Dictionary<string, string>? metric) {
        if (metric is null) {
            return "value";
        }

        List<string> parts = [];
        foreach (string key in LabelKeys) {
            if (metric.TryGetValue(key, out string? value) && !string.IsNullOrEmpty(value)) {
                parts.Add(value);
            }
        }

        return parts.Count > 0 ? string.Join(' ', parts) : "value";
    }

    private static IReadOnlyList<ChartPoint> Samples(JsonElement values) {
        List<ChartPoint> points = [];
        if (values.ValueKind != JsonValueKind.Array) {
            return points;
        }

        foreach (JsonElement sample in values.EnumerateArray()) {
            if (sample.ValueKind != JsonValueKind.Array || sample.GetArrayLength() < 2) {
                continue;
            }

            JsonElement at = sample[0];
            if (at.ValueKind != JsonValueKind.Number) {
                continue;
            }

            DateTimeOffset instant = DateTimeOffset.FromUnixTimeMilliseconds((long)Math.Round(at.GetDouble() * 1000));
            points.Add(new ChartPoint(instant, Value(sample[1])));
        }

        return points;
    }

    // Prometheus writes every value as a string, and uses NaN/±Inf for "no data" and "too small to
    // represent"; a gap is a gap, not a number the chart should try to draw.
    private static double? Value(JsonElement value) {
        string? text = value.ValueKind switch {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
            && double.IsFinite(parsed)
            ? parsed
            : null;
    }

    private sealed record PrometheusResponse(
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("errorType")] string? ErrorType,
        [property: JsonPropertyName("error")] string? Error,
        [property: JsonPropertyName("data")] PrometheusData? Data);

    private sealed record PrometheusData(
        [property: JsonPropertyName("resultType")] string? ResultType,
        [property: JsonPropertyName("result")] List<PrometheusResult>? Result);

    private sealed record PrometheusResult(
        [property: JsonPropertyName("metric")] Dictionary<string, string>? Metric,
        [property: JsonPropertyName("values")] JsonElement Values);
}
