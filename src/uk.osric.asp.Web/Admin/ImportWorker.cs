// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Admin;

using Microsoft.Extensions.DependencyInjection;

/// <summary>Runs approved imports one at a time, each in its own scope.</summary>
/// <remarks>
/// One at a time because <c>CompileSlots</c> is one: a second import would only wait behind the
/// first, and serialising the compiles keeps the progress page honest. The processor is scoped
/// because <see cref="Pages.PageImageBuilder"/> and the batch store are.
/// </remarks>
internal sealed class ImportWorker(
    ImportQueue queue,
    ImportStageStore stages,
    IServiceScopeFactory scopeFactory,
    ILogger<ImportWorker> logger) : BackgroundService {

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        try {
            await foreach (string stageId in queue.ReadAllAsync(stoppingToken)) {
                ImportStage? stage = stages.Get(stageId);
                if (stage is null) {
                    logger.LogWarning("Import {StageId} expired before it could run", stageId);
                    continue;
                }

                try {
                    await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
                    ImportProcessor processor = scope.ServiceProvider.GetRequiredService<ImportProcessor>();
                    await processor.ProcessAsync(stage, stoppingToken);
                } catch (Exception exception) {
                    logger.LogError(exception, "Import {StageId} escaped its processor", stageId);
                    stage.Fail("The import failed unexpectedly, so none of the selected pages changed.");
                }
            }
        } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
        }
    }
}
