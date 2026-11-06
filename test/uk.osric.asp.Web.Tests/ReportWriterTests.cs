// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NUnit.Framework;
using System.Security.Claims;
using uk.osric.asp.Web.Health;

[TestFixture]
public sealed class HealthReportWriterTests {

    private const string Secret = "Data Source=/srv/secret/storage.db";

    [Test]
    public async Task AnAnonymousCallerGetsNoExceptionOrData() {
        DefaultHttpContext context = new();
        context.Response.Body = new MemoryStream();

        await ReportWriter.WriteAsync(context, Report());

        string body = await TestHttp.ReadBodyAsync(context);
        Assert.That(body, Does.Not.Contain(Secret));
        Assert.That(body, Does.Contain("\"error\": null"));
        Assert.That(body, Does.Contain("\"data\": null"));
    }

    [Test]
    public async Task ASignedInCallerGetsTheExceptionAndData() {
        DefaultHttpContext context = new();
        context.Response.Body = new MemoryStream();
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "owner")], "Cookies"));

        await ReportWriter.WriteAsync(context, Report());

        string body = await TestHttp.ReadBodyAsync(context);
        Assert.That(body, Does.Contain(Secret));
        Assert.That(body, Does.Contain("storage unreachable"));
    }

    [Test]
    public async Task TheStatusAndDurationAreWrittenEitherWay() {
        DefaultHttpContext context = new();
        context.Response.Body = new MemoryStream();

        await ReportWriter.WriteAsync(context, Report());

        string body = await TestHttp.ReadBodyAsync(context);
        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status503ServiceUnavailable));
        Assert.That(body, Does.Contain("\"status\": \"Unhealthy\""));
        Assert.That(body, Does.Contain("\"storage\""));
        Assert.That(body, Does.Contain("\"durationMs\""));
    }

    private static HealthReport Report() {
        HealthReportEntry entry = new(
            HealthStatus.Unhealthy,
            "storage unreachable",
            TimeSpan.FromMilliseconds(12),
            new InvalidOperationException(Secret),
            new Dictionary<string, object> { ["path"] = Secret });

        return new HealthReport(
            new Dictionary<string, HealthReportEntry>(StringComparer.Ordinal) { ["storage"] = entry },
            TimeSpan.FromMilliseconds(12));
    }
}
