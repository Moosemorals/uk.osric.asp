// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Extensions.Diagnostics.HealthChecks;
using NUnit.Framework;
using uk.osric.asp.Web.Health;
using uk.osric.asp.Web.Pages;

[TestFixture]
public sealed class AbandonedRenderHealthCheckTests {

    private static readonly HealthCheckContext Context = new() {
        Registration = new HealthCheckRegistration("pages", _ => null!, null, null),
    };

    [Test]
    public async Task NoAbandonedRendersIsHealthyAsync() {
        AbandonedRenderHealthCheck check = new(new PageHealth(2));

        HealthCheckResult result = await check.CheckHealthAsync(Context);

        Assert.That(result.Status, Is.EqualTo(HealthStatus.Healthy));
    }

    [Test]
    public async Task AbandonedRendersBelowTheLimitStayHealthyAsync() {
        PageHealth pages = new(2);
        pages.Register();
        AbandonedRenderHealthCheck check = new(pages);

        HealthCheckResult result = await check.CheckHealthAsync(Context);

        Assert.That(result.Status, Is.EqualTo(HealthStatus.Healthy));
    }

    [Test]
    public async Task ReachingTheLimitIsUnhealthyAsync() {
        PageHealth pages = new(2);
        pages.Register();
        pages.Register();
        AbandonedRenderHealthCheck check = new(pages);

        HealthCheckResult result = await check.CheckHealthAsync(Context);

        Assert.That(result.Status, Is.EqualTo(HealthStatus.Unhealthy));
        Assert.That(result.Description, Does.Contain("2"));
    }

    [Test]
    public async Task APageThatFinishesAfterBeingAbandonedRestoresHealthAsync() {
        PageHealth pages = new(1);
        pages.Register();
        AbandonedRenderHealthCheck check = new(pages);
        Assert.That((await check.CheckHealthAsync(Context)).Status, Is.EqualTo(HealthStatus.Unhealthy));

        pages.Release();

        Assert.That((await check.CheckHealthAsync(Context)).Status, Is.EqualTo(HealthStatus.Healthy));
    }
}
