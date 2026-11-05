// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Agent;

internal sealed class AgentRunJournalCleanupService(
    AgentRunRegistry registry,
    ILogger<AgentRunJournalCleanupService> logger) : BackgroundService {

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        using PeriodicTimer timer = new(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken)) {
            registry.RemoveExpired();
            logger.LogDebug("Expired agent run journals were cleaned up");
        }
    }
}
