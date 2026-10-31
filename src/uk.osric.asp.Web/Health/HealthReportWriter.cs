// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Health;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Text.Json;

/// <summary>Renders a health report as the JSON body of the <c>/heath</c> response.</summary>
public static class HealthReportWriter {

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) {
        WriteIndented = true,
    };

    /// <summary>Writes the report, answering 200 when healthy and 503 otherwise.</summary>
    public static async Task WriteAsync(HttpContext context, HealthReport report) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(report);

        context.Response.ContentType = "application/json; charset=utf-8";
        if (report.Status == HealthStatus.Healthy) {
            context.Response.StatusCode = StatusCodes.Status200OK;
        } else {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        }

        List<CheckResult> checks = [];
        foreach ((string name, HealthReportEntry entry) in report.Entries) {
            checks.Add(new CheckResult(name, entry.Status.ToString(), entry.Description,
                entry.Duration.TotalMilliseconds, entry.Exception?.Message, entry.Data));
        }

        HealthPayload payload = new(report.Status.ToString(), report.TotalDuration.TotalMilliseconds, checks);
        await context.Response.WriteAsJsonAsync(payload, JsonOptions, context.RequestAborted);
    }

    private sealed record CheckResult(string Name, string Status, string? Description,
        double DurationMs, string? Error, IReadOnlyDictionary<string, object>? Data);

    private sealed record HealthPayload(string Status, double TotalDurationMs, IReadOnlyList<CheckResult> Results);
}
