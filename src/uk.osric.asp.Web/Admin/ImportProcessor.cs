// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Admin;

using System.Diagnostics;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Pages;

/// <summary>Compiles an approved batch and commits it in one transaction.</summary>
/// <remarks>
/// <para>
/// The split is the whole design: compilation writes nothing, so a compile failure, a compiler fault,
/// a cap or a host crash leaves storage untouched; then the whole batch is written in one
/// transaction. A page that does not compile is a stored revision with diagnostics, not a reason to
/// stop.
/// </para>
/// <para>
/// Records <c>page.import.duration</c> for the batch as a whole.
/// </para>
/// </remarks>
internal sealed class ImportProcessor(
    PageImageBuilder images,
    IPageBatchStore batches,
    PageMetrics metrics,
    ArchiveOptions options,
    ILogger<ImportProcessor> logger) {

    /// <summary>Runs one approved batch.</summary>
    /// <param name="stage">The stage holding the archive and its results.</param>
    /// <param name="cancellationToken">Cancels the batch.</param>
    public async Task ProcessAsync(ImportStage stage, CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(stage);

        // The stage's scope is the worker's: it is opened before the stage is looked up and outlives
        // this call, so opening it again here would put two identical objects on every line.
        long start = Stopwatch.GetTimestamp();
        ImportStageSnapshot snapshot = stage.Snapshot();
        stage.BeginCompiling();
        logger.LogInformation("Import {StageId} compiling {Selected} pages", stage.Id, snapshot.Selected.Count);

        List<PageBatchSave> batch = [];
        long compiledBytes = 0;
        int compiled = 0;

        try {
            foreach (PagePath path in snapshot.Selected) {
                cancellationToken.ThrowIfCancellationRequested();

                if (stage.Find(path) is not { } entry) {
                    throw new InvalidOperationException($"Import {stage.Id} has no staged page for {path.Key}.");
                }

                (PageImage? image, ImportPageStatus status) = Compile(stage.Id, path, entry.Source, cancellationToken);
                compiled++;
                compiledBytes += image?.Bytes?.Length ?? 0;
                stage.Record(new ImportPageResult(path, status, image?.Diagnostics ?? []));

                if (compiledBytes > options.MaxCompiledBytes) {
                    stage.Fail("The compiled pages would need more room than this site holds for one import, so nothing was saved. Import fewer pages at a time.");
                    metrics.RecordImport(PageMetrics.ImportAborted, Stopwatch.GetElapsedTime(start));
                    logger.LogWarning("Import {StageId} aborted after {Compiled} pages: {Bytes} compiled bytes", stage.Id, compiled, compiledBytes);
                    return;
                }

                batch.Add(new PageBatchSave(PageTemplate.NewRevision(path, entry.Source, image, images.RuntimeId), entry.State));
            }

            stage.BeginSaving();
            PageBatchResult result = await batches.SaveBatchAsync(batch, cancellationToken);
            TimeSpan elapsed = Stopwatch.GetElapsedTime(start);
            if (result.IsCommitted) {
                stage.Commit($"{DescribePages(batch.Count)} saved.");
                metrics.RecordImport(PageMetrics.ImportCommitted, elapsed);
                logger.LogInformation("Import {StageId} committed {Committed} pages after compiling {Compiled} in {ElapsedMs:0.0} ms",
                    stage.Id, batch.Count, compiled, elapsed.TotalMilliseconds);
            } else {
                stage.Fail("The batch was refused because a page's merged state would not fit, so none of the selected pages changed. Import fewer pages, or fewer state keys, at a time.", result.Refused);
                metrics.RecordImport(PageMetrics.ImportFailed, elapsed);
                logger.LogWarning("Import {StageId} was refused for {Paths}", stage.Id, string.Join(", ", result.Refused.Select(path => path.Key)));
            }
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            stage.Fail("The import stopped before it finished, so none of the selected pages changed.");
            metrics.RecordImport(PageMetrics.ImportAborted, Stopwatch.GetElapsedTime(start));
        } catch (Exception exception) {
            stage.Fail("The pages could not be saved, so none of the selected pages changed.");
            metrics.RecordImport(PageMetrics.ImportFailed, Stopwatch.GetElapsedTime(start));
            logger.LogError(exception, "Import {StageId} failed while saving", stage.Id);
        }
    }

    private (PageImage? Image, ImportPageStatus Status) Compile(string stageId, PagePath path, string source, CancellationToken cancellationToken) {
        try {
            PageImage image = images.Build(path, source, PageMetrics.ReasonImport, cancellationToken);
            if (image.Bytes is null) {
                return (image, ImportPageStatus.CompiledWithErrors);
            }

            return (image, image.Diagnostics.Count > 0
                ? ImportPageStatus.CompiledWithWarnings
                : ImportPageStatus.Compiled);
        } catch (InvalidOperationException exception) {
            // A compiler fault is our bug, but the author's text is never lost to it: the source is
            // kept and the revision is stored with no image, exactly as an editor save does.
            logger.LogError(exception, "Compiling {Path} during import {StageId} failed; its source is kept", path.Key, stageId);
            return (null, ImportPageStatus.CompilerFault);
        }
    }

    private static string DescribePages(int count) =>
        count == 1 ? "1 page" : $"{count.ToString(System.Globalization.CultureInfo.InvariantCulture)} pages";
}
