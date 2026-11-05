// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Authentication;

/// <summary>Drops stored authentication tickets that expired without a request to remove them.</summary>
internal sealed class AuthTicketCleanupService(
    AuthTicketStore tickets,
    TimeProvider clock,
    ILogger<AuthTicketCleanupService> logger) : BackgroundService {

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        using PeriodicTimer timer = new(TimeSpan.FromHours(1));
        while (await timer.WaitForNextTickAsync(stoppingToken)) {
            try {
                int removed = tickets.RemoveExpired(clock.GetUtcNow().UtcDateTime);
                if (removed > 0) {
                    logger.LogDebug("Removed {Count} expired authentication tickets", removed);
                }
            } catch (Exception exception) {
                // A sweep that cannot run must not take the host with it; the next hour retries.
                logger.LogWarning(exception, "Could not remove expired authentication tickets.");
            }
        }
    }
}
