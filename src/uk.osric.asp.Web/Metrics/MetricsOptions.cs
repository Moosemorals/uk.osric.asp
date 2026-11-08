// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Metrics;

/// <summary>Where the dashboard reads metrics from and how its axis labels are written.</summary>
internal sealed record MetricsOptions {

    /// <summary>The Prometheus address inside the podman network.</summary>
    public const string DefaultPrometheusUrl = "http://prometheus:9090";

    /// <summary>The zone the time axis is labelled in.</summary>
    public const string DefaultTimeZoneId = "Europe/London";

    /// <summary>The Prometheus base address; queries are appended to it.</summary>
    public required Uri PrometheusUrl { get; init; }

    /// <summary>The zone resolved once at startup, falling back to UTC rather than failing.</summary>
    public required TimeZoneInfo TimeZone { get; init; }

    /// <summary>Reads <c>Metrics</c>, falling back to the defaults when it is missing or unusable.</summary>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="logger">Where a fallback is reported, because it is otherwise invisible.</param>
    public static MetricsOptions FromConfiguration(IConfiguration configuration, ILogger logger) {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(logger);

        string? url = configuration["Metrics:PrometheusUrl"];
        Uri prometheusUrl = Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed)
            ? parsed
            : new Uri(DefaultPrometheusUrl);

        string timeZoneId = configuration["Metrics:TimeZone"] is { Length: > 0 } configured
            ? configured
            : DefaultTimeZoneId;

        TimeZoneInfo zone;
        try {
            zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        } catch (TimeZoneNotFoundException) {
            logger.LogWarning(
                "Metrics:TimeZone {TimeZoneId} is unknown on this host; axis labels will use UTC.",
                timeZoneId);
            zone = TimeZoneInfo.Utc;
        } catch (InvalidTimeZoneException) {
            logger.LogWarning(
                "Metrics:TimeZone {TimeZoneId} is unreadable on this host; axis labels will use UTC.",
                timeZoneId);
            zone = TimeZoneInfo.Utc;
        }

        return new MetricsOptions {
            PrometheusUrl = prometheusUrl,
            TimeZone = zone,
        };
    }
}
