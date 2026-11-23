// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Work;

using System.Diagnostics;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Pages;

/// <summary>Why a job's deadline was reached, when it was not the worker returning.</summary>
internal enum JobEnd {

    /// <summary>The worker returned on its own.</summary>
    Completed,

    /// <summary>A page, or the host stopping, asked for it to end.</summary>
    Cancelled,

    /// <summary>It published and received nothing for <c>Pages:Jobs:SilenceSeconds</c>.</summary>
    Silence,

    /// <summary>It ran for <c>Pages:Jobs:MaxDurationSeconds</c>.</summary>
    Ceiling,
}

/// <summary>The two clocks a job runs against, and how it ended.</summary>
/// <remarks>
/// <para>
/// The silence clock is what notices a stall and is re-armed by everything the worker does; the
/// ceiling is what ends work that never stops. A wait in <see cref="IWorkerContext.ReceiveAsync"/> is
/// not silence — a worker waiting on its owner is doing as it was told — so a wait pauses the silence
/// clock and only the ceiling bounds it.
/// </para>
/// <para>
/// The reason is recorded before the token is cancelled, so whichever await the worker unwinds at can
/// still say which clock ended it.
/// </para>
/// </remarks>
internal sealed class JobLifetime : IDisposable {

    private readonly CancellationTokenSource _deadline = new();
    private readonly PageJobOptions _options;
    private readonly Lock _sync = new();
    private readonly long _started = Stopwatch.GetTimestamp();
    private readonly Timer _timer;
    private JobEnd _end = JobEnd.Completed;
    private bool _armedForCeiling;
    private bool _disposed;
    private bool _paused;

    /// <summary>Starts the clocks.</summary>
    /// <param name="options">The job's bounds.</param>
    public JobLifetime(PageJobOptions options) {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
        _timer = new Timer(_ => Expire(), state: null, Timeout.Infinite, Timeout.Infinite);
        ReArm();
    }

    /// <summary>What ended the job, or <see cref="JobEnd.Completed"/> while it is still running.</summary>
    public JobEnd End {
        get {
            lock (_sync) {
                return _end;
            }
        }
    }

    /// <summary>The token a worker waits against.</summary>
    public CancellationToken Token => _deadline.Token;

    /// <summary>The job's ceiling, from the instant it started.</summary>
    public TimeSpan Total => _options.MaxDuration;

    /// <summary>What is left of the ceiling, never below zero.</summary>
    public TimeSpan Remaining {
        get {
            TimeSpan left = _options.MaxDuration - Stopwatch.GetElapsedTime(_started);
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }
    }

    /// <summary>Restarts the silence clock, after a publish or a returned receive.</summary>
    public void ReArm() {
        bool fire;
        lock (_sync) {
            fire = Arm();
        }

        if (fire) {
            Trigger();
        }
    }

    /// <summary>Stops the silence clock, because the worker is waiting for its owner.</summary>
    /// <remarks>
    /// The ceiling keeps running: a wait is bounded by how long the job may live, not by how quiet it
    /// is allowed to be, so an indefinite wait still ends at <c>MaxDurationSeconds</c>.
    /// </remarks>
    public void Pause() {
        bool fire;
        lock (_sync) {
            _paused = true;
            fire = Arm();
        }

        if (fire) {
            Trigger();
        }
    }

    /// <summary>Restarts the silence clock after a wait.</summary>
    public void Resume() {
        bool fire;
        lock (_sync) {
            _paused = false;
            fire = Arm();
        }

        if (fire) {
            Trigger();
        }
    }

    /// <summary>Ends the job because somebody asked it to stop.</summary>
    public void Cancel() {
        lock (_sync) {
            _end = JobEnd.Cancelled;
        }

        Trigger();
    }

    /// <inheritdoc/>
    public void Dispose() {
        lock (_sync) {
            if (_disposed) {
                return;
            }

            _disposed = true;
        }

        _timer.Dispose();
        _deadline.Dispose();
    }

    // Returns whether the caller must cancel, so the cancellation never runs under the lock: the
    // callbacks it invokes belong to worker code.
    private bool Arm() {
        if (_disposed) {
            return false;
        }

        TimeSpan remaining = _options.MaxDuration - Stopwatch.GetElapsedTime(_started);
        if (remaining <= TimeSpan.Zero) {
            _end = JobEnd.Ceiling;
            return true;
        }

        // A paused wait is armed for the ceiling alone: it may not be cut for silence, and it may not
        // outlive the job either.
        _armedForCeiling = _paused || remaining <= _options.Silence;
        _ = _timer.Change(_armedForCeiling ? remaining : _options.Silence, Timeout.InfiniteTimeSpan);

        return false;
    }

    private void Expire() {
        lock (_sync) {
            if (_disposed) {
                return;
            }

            _end = _armedForCeiling || _options.MaxDuration - Stopwatch.GetElapsedTime(_started) <= TimeSpan.Zero
                ? JobEnd.Ceiling
                : JobEnd.Silence;
        }

        Trigger();
    }

    private void Trigger() {
        if (_deadline.IsCancellationRequested) {
            return;
        }

        try {
            _deadline.Cancel();
        } catch (ObjectDisposedException) {
            // The job has already ended; nothing is waiting on the token any more.
        }
    }
}
