// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

using Microsoft.Extensions.DependencyInjection;

/// <summary>Reads the run queue and runs jobs within the configured concurrency.</summary>
internal sealed class RunWorker(
    RunQueue queue,
    IServiceScopeFactory scopeFactory,
    AssistantOptions options,
    ILogger<RunWorker> logger) : BackgroundService {

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        using SemaphoreSlim activeSlots = new(options.MaxActiveRuns);
        HashSet<Task> activeRuns = [];

        try {
            await foreach (QueuedRun queued in queue.ReadAllAsync(stoppingToken)) {
                await activeSlots.WaitAsync(stoppingToken);
                Task run = RunAsync(queued, activeSlots, stoppingToken);
                activeRuns.Add(run);
                activeRuns.RemoveWhere(task => task.IsCompleted);
            }
        } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
        }

        await Task.WhenAll(activeRuns);
    }

    private async Task RunAsync(QueuedRun queued, SemaphoreSlim activeSlots, CancellationToken stoppingToken) {
        try {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            RunProcessor processor = scope.ServiceProvider.GetRequiredService<RunProcessor>();
            await processor.RunAsync(queued, stoppingToken);
        } catch (Exception exception) {
            logger.LogError(exception, "Assistant run {RunId} in conversation {ConversationId} escaped its processor", queued.RunId, queued.ConversationId);
        } finally {
            activeSlots.Release();
        }
    }
}
