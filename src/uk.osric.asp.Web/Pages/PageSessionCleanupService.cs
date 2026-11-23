// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

/// <summary>Drops page sessions that expired, and trims the table when it is over its limit.</summary>
/// <remarks>
/// A timer rather than a sweep on a request: a visitor should not pay for housekeeping, and a site
/// whose visitors stop arriving still needs its rows to go. A sweep that fails is logged and retried
/// on the next tick rather than taking the host down.
/// </remarks>
internal sealed class PageSessionCleanupService(
    IPageSessionStore sessions,
    IStoreValues values,
    IStoreMessages messages,
    IStoreObjects objects,
    PageStoresOptions stores,
    ILogger<PageSessionCleanupService> logger) : BackgroundService {

    private static readonly TimeSpan Period = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        using PeriodicTimer timer = new(Period);

        while (await timer.WaitForNextTickAsync(stoppingToken)) {
            try {
                PageSessionSweep sweep = await sessions.RemoveExpiredAsync(stoppingToken);
                int valueRows = await values.RemoveExpiredAsync(
                    TimeSpan.FromDays(stores.Visitor.BrowserLifetimeDays), stoppingToken);
                int messageRows = await messages.RemoveExpiredAsync(
                    TimeSpan.FromDays(stores.Visitor.BrowserLifetimeDays), stoppingToken);
                int objectRows = await objects.RemoveExpiredAsync(
                    TimeSpan.FromDays(stores.Visitor.BrowserLifetimeDays), stoppingToken);

                if (sweep.Expired > 0 || sweep.AnonymousEvicted > 0 || sweep.SignedInEvicted > 0
                    || valueRows > 0 || messageRows > 0 || objectRows > 0) {
                    logger.LogInformation(
                        "Removed {Expired} expired page sessions, evicted {Anonymous} anonymous and {SignedIn} signed-in sessions, and removed {Values} expired or stale values, {Messages} expired or stale messages and {Objects} expired or stale objects",
                        sweep.Expired, sweep.AnonymousEvicted, sweep.SignedInEvicted, valueRows, messageRows, objectRows);
                }
            } catch (Exception exception) {
                logger.LogWarning(exception, "Could not sweep page sessions.");
            }
        }
    }
}
