// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Health;

using Microsoft.Extensions.Diagnostics.HealthChecks;
using uk.osric.asp.Web.Pages;

/// <summary>Reports unhealthy once pages have wedged too many threads for too long.</summary>
/// <remarks>
/// A page that never returns cannot be interrupted, so the process that owns it has to die. This is
/// how the site asks for that: the podman health check probes <c>/heath</c>, and three failures
/// restart the container. It deliberately does not guess at CPU or memory — those belong to the
/// container's own limits.
/// </remarks>
public sealed class AbandonedRenderHealthCheck(PageHealth pages) : IHealthCheck {

    /// <inheritdoc/>
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) {
        int abandoned = pages.Abandoned;

        return Task.FromResult(abandoned >= pages.Limit
            ? HealthCheckResult.Unhealthy($"{abandoned} page renders have been abandoned")
            : HealthCheckResult.Healthy($"{abandoned} abandoned page renders"));
    }
}
