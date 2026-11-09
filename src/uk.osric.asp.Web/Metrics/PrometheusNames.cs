// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Metrics;

/// <summary>The metric family names the Prometheus exporter writes for an instrument.</summary>
/// <remarks>
/// The exporter sanitises the dotted name to underscores and appends the unit where it names one:
/// <c>s</c> becomes <c>_seconds</c>, <c>By</c> becomes <c>_bytes</c>, and a dimensionless <c>1</c> or
/// an annotation such as <c>{step}</c> adds nothing. A histogram then carries <c>_bucket</c>,
/// <c>_count</c> and <c>_sum</c>, and a counter carries <c>_total</c>.
/// <c>MetricNamesTests</c> reads the live <c>/metrics</c> body and proves this mapping against the
/// exporter, so a rename or a unit change fails a test rather than emptying a panel.
/// </remarks>
internal static class PrometheusNames {

    /// <summary>The family prefix an instrument is exported under, without a type suffix.</summary>
    /// <param name="instrument">The instrument.</param>
    internal static string Family(MetricInstrument instrument) {
        ArgumentNullException.ThrowIfNull(instrument);

        string unit = instrument.Unit switch {
            "s" => "_seconds",
            "By" => "_bytes",
            _ => string.Empty,
        };

        return instrument.Name.Replace('.', '_') + unit;
    }

    /// <summary>The family a histogram's buckets are exported under.</summary>
    /// <param name="instrument">The instrument.</param>
    internal static string Buckets(MetricInstrument instrument) => Family(instrument) + "_bucket";

    /// <summary>The family a counter's total is exported under.</summary>
    /// <param name="instrument">The instrument.</param>
    internal static string Total(MetricInstrument instrument) => Family(instrument) + "_total";
}
