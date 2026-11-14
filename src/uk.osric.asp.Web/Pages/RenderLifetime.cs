// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using System.Diagnostics;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;

/// <summary>The two clocks and the slot a page holds while it runs, and how its stream ended.</summary>
/// <remarks>
/// <para>
/// Before the first flush this is the render's absolute budget and the render slot. From the commit
/// it is the stream's silence clock, re-armed by every flush, and an absolute ceiling, held against a
/// stream slot instead. One owner for the whole of it is what keeps the slot accounting from drifting
/// between the context, the renderer and the endpoint.
/// </para>
/// <para>
/// The deadline is one linked source for the request and the budget, so the page's waits, its
/// capabilities and the renderer's expiry all stop at the same moment. The source outlives an
/// abandoned page: the page's continuation disposes it when it finally stops, because disposing it
/// under a running page would pull the token out from under it.
/// </para>
/// </remarks>
internal sealed class RenderLifetime : IDisposable {

    private readonly PageBudget _budget;
    private readonly PageMetrics _metrics;
    private readonly PagePath _path;
    private readonly CancellationTokenSource _deadline;
    private readonly StreamOptions _stream;
    private Action? _onCommit;
    private bool _armedForCeiling;
    private bool _abandoned;
    private bool _renderSlotHeld;
    private bool _streamSlotHeld;
    private bool _streaming;
    private bool _streamRecorded;
    private long _streamStarted;
    private string _outcome = PageMetrics.StreamCompleted;

    /// <summary>Starts the lifetime with the render slot already taken.</summary>
    /// <param name="budget">The slots the lifetime releases and takes.</param>
    /// <param name="path">The page the request asked for, for the metric and the log.</param>
    /// <param name="metrics">Where the stream's own duration is recorded.</param>
    /// <param name="deadline">The linked source already armed with the render's budget.</param>
    public RenderLifetime(PageBudget budget, PagePath path, PageMetrics metrics, CancellationTokenSource deadline) {
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(deadline);

        _budget = budget;
        _path = path;
        _metrics = metrics;
        _deadline = deadline;
        _stream = budget.StreamOptions;
        _renderSlotHeld = true;
    }

    /// <summary>The deadline every layer and capability in this render waits against.</summary>
    public CancellationToken Token => _deadline.Token;

    /// <summary>Whether this render has committed and is holding a stream slot.</summary>
    public bool IsStreaming => _streaming;

    /// <summary>How the stream ended, or <c>completed</c> while it is still running.</summary>
    public string Outcome => _outcome;

    /// <summary>Registers what records the render's own duration at the commit.</summary>
    /// <param name="onCommit">The callback; it runs once, before the response starts.</param>
    public void OnCommit(Action onCommit) {
        ArgumentNullException.ThrowIfNull(onCommit);

        _onCommit = onCommit;
    }

    /// <summary>Moves the render from its budget to the stream's clocks.</summary>
    /// <returns>A task that completes when the stream slot is held.</returns>
    /// <exception cref="PageStreamRefusedException">Every stream slot is taken; nothing is committed.</exception>
    public async Task BeginStreamAsync() {
        if (_streaming) {
            ReArm();
            return;
        }

        if (!await _budget.StreamSlots.WaitAsync(0, Token)) {
            throw new PageStreamRefusedException(
                $"{_path.Key} cannot start a live stream: every stream slot on the site is taken.");
        }

        _streamSlotHeld = true;

        if (_renderSlotHeld) {
            _budget.RenderSlots.Release();
            _renderSlotHeld = false;
        }

        _streaming = true;
        _streamStarted = Stopwatch.GetTimestamp();
        _outcome = PageMetrics.StreamCompleted;
        RearmLocked();
        _onCommit?.Invoke();
    }

    /// <summary>Restarts the silence clock after a flush.</summary>
    public void ReArm() {
        if (_streaming) {
            RearmLocked();
        }
    }

    /// <summary>Marks the stream as ended normally, or by the client leaving.</summary>
    /// <param name="clientLeft">Whether the request was aborted rather than the page returning.</param>
    public void Complete(bool clientLeft) => _outcome = clientLeft ? PageMetrics.StreamClientLeft : PageMetrics.StreamCompleted;

    /// <summary>Marks the stream as ended by a page fault.</summary>
    public void Fail() => _outcome = PageMetrics.StreamError;

    /// <summary>Marks the stream as ended by the clock that fired while the last bytes were written.</summary>
    public void Truncated() {
        if (_streaming) {
            _outcome = _armedForCeiling ? PageMetrics.StreamCeiling : PageMetrics.StreamSilence;
        }
    }

    /// <summary>Records that the render gave up on the page and why.</summary>
    /// <param name="clientLeft">Whether the visitor left rather than the deadline firing.</param>
    public void Abandon(bool clientLeft) {
        _abandoned = true;
        if (!_streaming) {
            return;
        }

        _outcome = clientLeft
            ? PageMetrics.StreamClientLeft
            : _armedForCeiling ? PageMetrics.StreamCeiling : PageMetrics.StreamSilence;
    }

    /// <summary>Releases the slots and records the stream's lifetime.</summary>
    public void Dispose() {
        if (_renderSlotHeld) {
            _budget.RenderSlots.Release();
            _renderSlotHeld = false;
        }

        if (_streamSlotHeld) {
            _budget.StreamSlots.Release();
            _streamSlotHeld = false;
        }

        if (_streaming && !_streamRecorded) {
            _metrics.RecordStream(_path, _outcome, Stopwatch.GetElapsedTime(_streamStarted));
            _streamRecorded = true;
        }

        // An abandoned page may still be running; its continuation disposes the source when it stops.
        if (!_abandoned) {
            _deadline.Dispose();
        }
    }

    /// <summary>Disposes the deadline once an abandoned page has finally stopped.</summary>
    public void DisposeDeadline() => _deadline.Dispose();

    private void RearmLocked() {
        TimeSpan elapsed = Stopwatch.GetElapsedTime(_streamStarted);
        TimeSpan remaining = _stream.Max - elapsed;

        if (remaining <= TimeSpan.Zero) {
            _armedForCeiling = true;
            _deadline.Cancel();
            return;
        }

        _armedForCeiling = remaining <= _stream.Silence;
        _deadline.CancelAfter(_armedForCeiling ? remaining : _stream.Silence);
    }
}
