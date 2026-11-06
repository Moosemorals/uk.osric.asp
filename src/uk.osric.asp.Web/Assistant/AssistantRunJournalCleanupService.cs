// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

/// <summary>Drops expired run journals on a timer.</summary>
internal sealed class AssistantRunJournalCleanupService(
    AssistantRunRegistry registry,
    ILogger<AssistantRunJournalCleanupService> logger) : BackgroundService {

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        using PeriodicTimer timer = new(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken)) {
            registry.RemoveExpired();
            logger.LogDebug("Expired assistant run journals were cleaned up");
        }
    }
}
