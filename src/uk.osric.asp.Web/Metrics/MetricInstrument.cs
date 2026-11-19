// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Metrics;

/// <summary>One instrument a meter registers, as the catalogue and the drift test see it.</summary>
/// <param name="Name">The instrument name, which is also its panel's id.</param>
/// <param name="Kind">What sort of instrument it is, which decides how it is drawn.</param>
/// <param name="Unit">The unit the instrument records, in OpenTelemetry's notation.</param>
internal sealed record MetricInstrument(string Name, InstrumentKind Kind, string Unit);

/// <summary>The three kinds of instrument the site registers.</summary>
internal enum InstrumentKind {

    /// <summary>A distribution over buckets; drawn as a heatmap.</summary>
    Histogram,

    /// <summary>A monotonic count; drawn as a rate.</summary>
    Counter,

    /// <summary>A level at the moment it is read.</summary>
    Gauge,
}
