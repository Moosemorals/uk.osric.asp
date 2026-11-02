// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Metrics;

/// <summary>Runs PromQL over a range and returns charts-ready series.</summary>
internal interface IMetricsSource {

    /// <summary>Runs one query.</summary>
    /// <param name="query">The PromQL expression.</param>
    /// <param name="from">The start of the range.</param>
    /// <param name="to">The end of the range.</param>
    /// <param name="step">The spacing of the returned samples.</param>
    /// <param name="cancellationToken">Aborted when the request is abandoned.</param>
    /// <returns>The series, or why there are none; never throws for a remote failure.</returns>
    Task<MetricsQueryResult> QueryAsync(
        string query,
        DateTimeOffset from,
        DateTimeOffset to,
        TimeSpan step,
        CancellationToken cancellationToken);
}
