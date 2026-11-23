// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

/// <summary>Reads the site's own files at startup, so the first page that asks finds them.</summary>
/// <remarks>
/// <para>
/// The listener is already answering when this runs, so a start is not held up by a corpus parse; the
/// three reads run apart on the thread pool. A page that asks while a read is still running waits for
/// that read rather than starting a second, because the source is process-wide.
/// </para>
/// <para>
/// A source that cannot be read answers a value rather than throwing, so this service has nothing to
/// fail and the site starts as it would without it.
/// </para>
/// </remarks>
internal sealed class SiteFactsWarmUp(SiteFacts facts, ILogger<SiteFactsWarmUp> logger) : BackgroundService {

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        try {
            await facts.WarmAsync(stoppingToken);
        } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
            // The host is stopping before the warm-up finished; nothing is waiting on it any more.
        }

        logger.LogDebug("The site's facts are read");
    }
}
