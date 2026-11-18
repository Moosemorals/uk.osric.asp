// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Http;

using Microsoft.AspNetCore.Http.Features;

/// <summary>Keeps machine traffic out of the HTTP request duration metrics.</summary>
/// <remarks>
/// The container's health check and Prometheus both ask on a timer; left in the histogram those
/// requests read as visitor traffic and pull the duration buckets toward zero. Only the duration
/// instrument is suppressed, because the active-request gauge measures the server rather than the
/// site's traffic and these are real requests on it.
/// </remarks>
internal static class MachineTrafficMetrics {

    /// <summary>The path the health probe answers on.</summary>
    public const string HealthPath = "/health";

    /// <summary>The path Prometheus scrapes.</summary>
    public const string ScrapePath = "/metrics";

    private static readonly string[] Paths = [HealthPath, ScrapePath];

    /// <summary>Disables request duration metrics for a machine request, then continues the pipeline.</summary>
    /// <param name="context">The request being handled.</param>
    /// <param name="next">The rest of the pipeline.</param>
    public static async Task SkipAsync(HttpContext context, RequestDelegate next) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        // The feature is absent when nothing collects the hosting meter, as in a test that never
        // starts a host, and there is then nothing to disable.
        if (context.Features.Get<IHttpMetricsTagsFeature>() is { } metrics
            && Paths.Any(path => context.Request.Path.StartsWithSegments(path))) {
            metrics.MetricsDisabled = true;
        }

        await next(context);
    }
}
