// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Metrics;

using uk.osric.asp.Core.Charting;

/// <summary>What one query returned: data, or the reason there is none.</summary>
/// <remarks>
/// Failure is a value rather than an exception, because the status page is what gets opened when
/// things are broken and must survive Prometheus being down.
/// </remarks>
internal abstract record MetricsQueryResult;

/// <summary>The query answered with one series per labelled result.</summary>
/// <param name="Series">The series, possibly empty when the query matched nothing.</param>
internal sealed record MetricsMatrix(IReadOnlyList<ChartSeries> Series) : MetricsQueryResult;

/// <summary>The query could not be answered.</summary>
/// <param name="Reason">Text for the banner, written for the person reading the page.</param>
internal sealed record MetricsUnavailable(string Reason) : MetricsQueryResult;
