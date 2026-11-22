// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Work;

using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Pages;

/// <summary>One job's <see cref="IWorkerContext"/>: its identity, its capabilities and its channel.</summary>
/// <remarks>
/// It has no response members at all, because a job answers nobody. Its log entries are filed under the
/// page category with the job's own scope, so a worker's line is found beside the render that started
/// it.
/// </remarks>
internal sealed class WorkerContext(
    PageJob job,
    PageCapabilities capabilities,
    IPageStores stores,
    PageJobOptions options,
    ILogger logger) : IWorkerContext {

    /// <inheritdoc/>
    public string Id => job.Id;

    /// <inheritdoc/>
    public string Path => job.Page.Key;

    /// <inheritdoc/>
    public string Worker => job.Worker.Key;

    /// <inheritdoc/>
    public PageUser User => job.Owner;

    /// <inheritdoc/>
    public string? Input => job.Input;

    /// <inheritdoc/>
    public CancellationToken Aborted => job.Lifetime.Token;

    /// <inheritdoc/>
    public IPageCapabilities Capabilities => capabilities;

    /// <inheritdoc/>
    public IPageStores Stores => stores;

    /// <inheritdoc/>
    public void Log(string message) => logger.LogInformation("{Message}", message);

    /// <inheritdoc/>
    public void LogWarning(string message) => logger.LogWarning("{Message}", message);

    /// <inheritdoc/>
    public void LogError(string message) => logger.LogError("{Message}", message);

    /// <inheritdoc/>
    public Task<string?> ReceiveAsync(double? waitSeconds = null) {
        TimeSpan? wait = waitSeconds switch {
            null => null,
            <= 0 => TimeSpan.Zero,
            var seconds => TimeSpan.FromSeconds(Math.Min(seconds.Value, options.MaxWaitSeconds)),
        };

        return job.ReceiveAsync(wait, Aborted);
    }

    /// <inheritdoc/>
    public void Publish(string type, string dataJson) {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(dataJson);

        if (!job.Journal.Publish(type, dataJson)) {
            logger.LogWarning(
                "Job {JobId} published a {Bytes} byte event, which is larger than Pages:Jobs:MaxEventBytes allows, so it was dropped",
                job.Id,
                System.Text.Encoding.UTF8.GetByteCount(dataJson) + System.Text.Encoding.UTF8.GetByteCount(type));

            return;
        }

        job.Lifetime.ReArm();
    }
}
