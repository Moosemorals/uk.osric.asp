// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Web.Metrics;

/// <summary>Answers queries from a function, recording what was asked.</summary>
internal sealed class FakeMetricsSource(Func<string, MetricsQueryResult> respond) : IMetricsSource {

    /// <summary>How many queries were run, so a test can prove the page was cached.</summary>
    public int Calls { get; private set; }

    /// <summary>The start of the range the last query asked for.</summary>
    public DateTimeOffset From { get; private set; }

    /// <summary>The end of the range the last query asked for.</summary>
    public DateTimeOffset To { get; private set; }

    /// <summary>The spacing the last query asked for.</summary>
    public TimeSpan Step { get; private set; }

    public Task<MetricsQueryResult> QueryAsync(
        string query,
        DateTimeOffset from,
        DateTimeOffset to,
        TimeSpan step,
        CancellationToken cancellationToken) {

        Calls++;
        From = from;
        To = to;
        Step = step;
        return Task.FromResult(respond(query));
    }
}
