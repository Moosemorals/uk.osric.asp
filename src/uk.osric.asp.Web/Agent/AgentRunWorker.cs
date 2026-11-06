// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Agent;

using Microsoft.Extensions.DependencyInjection;

/// <summary>Reads the run queue and runs jobs within the configured concurrency.</summary>
internal sealed class AgentRunWorker(
    AgentRunQueue queue,
    IServiceScopeFactory scopeFactory,
    AgentOptions options,
    ILogger<AgentRunWorker> logger) : BackgroundService {

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        using SemaphoreSlim activeSlots = new(options.MaxActiveRuns);
        HashSet<Task> activeRuns = [];

        try {
            await foreach (AgentQueuedRun queued in queue.ReadAllAsync(stoppingToken)) {
                await activeSlots.WaitAsync(stoppingToken);
                Task run = RunAsync(queued, activeSlots, stoppingToken);
                activeRuns.Add(run);
                activeRuns.RemoveWhere(task => task.IsCompleted);
            }
        } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
        }

        await Task.WhenAll(activeRuns);
    }

    private async Task RunAsync(AgentQueuedRun queued, SemaphoreSlim activeSlots, CancellationToken stoppingToken) {
        try {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            AgentRunProcessor processor = scope.ServiceProvider.GetRequiredService<AgentRunProcessor>();
            await processor.RunAsync(queued, stoppingToken);
        } catch (Exception exception) {
            logger.LogError(exception, "Agent run {RunId} in conversation {ConversationId} escaped its processor", queued.RunId, queued.ConversationId);
        } finally {
            activeSlots.Release();
        }
    }
}
