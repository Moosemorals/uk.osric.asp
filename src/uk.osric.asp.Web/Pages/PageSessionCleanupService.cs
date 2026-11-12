// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

/// <summary>Drops page sessions that expired, and trims the table when it is over its cap.</summary>
/// <remarks>
/// A timer rather than a sweep on a request: a visitor should not pay for housekeeping, and a site
/// whose visitors stop arriving still needs its rows to go. A sweep that fails is logged and retried
/// on the next tick rather than taking the host down.
/// </remarks>
internal sealed class PageSessionCleanupService(
    IPageSessionStore sessions,
    ILogger<PageSessionCleanupService> logger) : BackgroundService {

    private static readonly TimeSpan Period = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        using PeriodicTimer timer = new(Period);

        while (await timer.WaitForNextTickAsync(stoppingToken)) {
            try {
                PageSessionSweep sweep = await sessions.RemoveExpiredAsync(stoppingToken);
                if (sweep.Expired > 0 || sweep.AnonymousEvicted > 0 || sweep.SignedInEvicted > 0) {
                    logger.LogInformation(
                        "Removed {Expired} expired page sessions and evicted {Anonymous} anonymous and {SignedIn} signed-in sessions",
                        sweep.Expired, sweep.AnonymousEvicted, sweep.SignedInEvicted);
                }
            } catch (Exception exception) {
                logger.LogWarning(exception, "Could not sweep page sessions.");
            }
        }
    }
}
