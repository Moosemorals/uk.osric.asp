// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Metrics;

using uk.osric.asp.Web.Configuration;

/// <summary>Where the dashboard reads metrics from, how long it keeps an answer and how it labels time.</summary>
/// <remarks>Every value comes from <c>Metrics</c>.</remarks>
internal sealed record MetricsOptions {

    /// <summary>The Prometheus base address; queries are appended to it.</summary>
    public required Uri PrometheusUrl { get; init; }

    /// <summary>The zone resolved once at startup.</summary>
    public required TimeZoneInfo TimeZone { get; init; }

    /// <summary>How long one Prometheus query may take.</summary>
    public required TimeSpan QueryTimeout { get; init; }

    /// <summary>How long a built status page is reused before it is queried again.</summary>
    public required TimeSpan CacheFor { get; init; }

    /// <summary>How many window pages the cache keeps before the oldest is dropped.</summary>
    public required int MaxCachedWindows { get; init; }

    /// <summary>Reads <c>Metrics</c>.</summary>
    /// <param name="configuration">The application configuration.</param>
    /// <exception cref="ConfigurationException">A value is missing or unusable.</exception>
    public static MetricsOptions FromConfiguration(IConfiguration configuration) {
        ArgumentNullException.ThrowIfNull(configuration);

        ConfigurationReader reader = new(configuration, "Metrics");
        MetricsOptions options = FromConfiguration(reader);
        reader.Complete();
        return options;
    }

    /// <summary>Reads <c>Metrics</c> into the caller's report.</summary>
    /// <param name="reader">A reader opened on <c>Metrics</c>, which the caller completes.</param>
    internal static MetricsOptions FromConfiguration(ConfigurationReader reader) {
        ArgumentNullException.ThrowIfNull(reader);

        string url = reader.Text("PrometheusUrl", "an absolute URL such as http://prometheus:9090");
        string timeZoneId = reader.Text("TimeZone", "an IANA zone such as Europe/London");
        MetricsOptions options = new() {
            PrometheusUrl = ReadUrl(reader, url),
            TimeZone = ReadTimeZone(reader, timeZoneId),
            QueryTimeout = TimeSpan.FromSeconds(reader.Decimal("QueryTimeoutSeconds", recommended: 2,
                minimum: 0.1, maximum: 60)),
            CacheFor = TimeSpan.FromSeconds(reader.Number("CacheSeconds", recommended: 15, minimum: 0, maximum: 3_600)),
            MaxCachedWindows = reader.Number("MaxCachedWindows", recommended: 16, minimum: 1, maximum: 1_024),
        };
        return options;
    }

    private static Uri ReadUrl(ConfigurationReader reader, string url) {
        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed)
            && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps)) {
            return parsed;
        }

        reader.Fail("PrometheusUrl", $"is \"{url}\", which is not an absolute http or https URL");
        return new Uri("http://prometheus:9090");
    }

    private static TimeZoneInfo ReadTimeZone(ConfigurationReader reader, string timeZoneId) {
        try {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        } catch (TimeZoneNotFoundException) {
            reader.Fail("TimeZone", $"is \"{timeZoneId}\", which this host does not know; use an IANA zone such as Europe/London");
        } catch (InvalidTimeZoneException) {
            reader.Fail("TimeZone", $"is \"{timeZoneId}\", which this host cannot read; use an IANA zone such as Europe/London");
        }

        return TimeZoneInfo.Utc;
    }
}
