// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using System.Text;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Core.Text;
using uk.osric.asp.Sdk;

/// <summary>The body one layer writes, and its way onto the wire.</summary>
/// <remarks>
/// <para>
/// The buffer, the lock that guards it, the shared budget, the commit and the abandonment flag are one
/// object because they only make sense together: a page's task can still be running after the render
/// gave up on it, so every write and the read that shapes the response happen under one gate, and an
/// abandoned page finds that gate closed rather than racing the response it no longer owns.
/// </para>
/// <para>
/// Whether a stream is allowed at all is the layer's question: the refusal names the page and its
/// remedy, so those messages stay on the context, and this type assumes the caller has checked
/// <see cref="CanStream"/>.
/// </para>
/// </remarks>
#pragma warning disable CA1001 // One render's layer; the flush gate never allocates a wait handle.
internal sealed class PageBody {

    private readonly CancellationToken _aborted;
    private readonly Lock _body = new();
    private readonly BodyBudget _bodyBudget;
    private readonly PageCommitState _commit;
    private readonly SemaphoreSlim _flushing = new(1, 1);
    private readonly PagePath _path;
    private readonly IPageBodySink? _sink;
    private readonly StringWriter _writer;

    // Written by the abandoning thread and read by the page's task, so the flag is volatile rather than
    // merely lock-protected.
    private volatile bool _abandoned;
    private RenderLifetime? _lifetime;

    /// <summary>Creates one layer's body.</summary>
    /// <param name="writer">The buffer the body is written to.</param>
    /// <param name="path">The layer's own key, used when a write overruns the budget.</param>
    /// <param name="budget">The allowance the whole chain shares.</param>
    /// <param name="commit">Whether this layer has committed, shared with the capabilities.</param>
    /// <param name="sink">Where a flushing layer writes, or <see langword="null"/> when it cannot stream.</param>
    /// <param name="aborted">The token from the request being served.</param>
    public PageBody(
        StringWriter writer,
        PagePath path,
        BodyBudget budget,
        PageCommitState commit,
        IPageBodySink? sink,
        CancellationToken aborted) {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(commit);

        _writer = writer;
        _path = path;
        _bodyBudget = budget;
        _commit = commit;
        _sink = sink;
        _aborted = aborted;
    }

    /// <summary>Whether the first flush has committed the response.</summary>
    public bool IsCommitted => _commit.Committed;

    /// <summary>Whether this layer has somewhere to stream to at all.</summary>
    public bool CanStream => _sink is not null;

    /// <summary>Gives the body the render's lifetime, so its I/O and its stream share one clock.</summary>
    /// <param name="lifetime">The lifetime the renderer opened for this request.</param>
    public void AttachLifetime(RenderLifetime lifetime) {
        ArgumentNullException.ThrowIfNull(lifetime);

        _lifetime = lifetime;
    }

    /// <summary>Stops the layer writing, once its render has been given up on.</summary>
    /// <remarks>
    /// The page cannot be stopped, so this is what keeps it from filling the buffer — or racing the
    /// response — after the site has answered without it.
    /// </remarks>
    public void Abandon() {
        lock (_body) {
            _abandoned = true;
        }
    }

    /// <summary>Appends one piece of the body, charging it to the shared budget.</summary>
    /// <param name="text">The text to append; it is already escaped as the caller intended.</param>
    /// <exception cref="InvalidOperationException">The write overran the budget.</exception>
    public void Write(string text) {
        lock (_body) {
            if (_abandoned) {
                return;
            }

            // The limit exists to bound what is held in memory before the response can be sent. Once the
            // page has committed, its body is on the wire and a total limit would defeat streaming.
            if (!_commit.Committed && !_bodyBudget.TryCharge(Utf8.SizeInBytes(text))) {
                throw new InvalidOperationException(
                    $"{_path.Key} wrote more than {_bodyBudget.MaxBytes} bytes; the render was stopped.");
            }

            _writer.Write(text);
        }
    }

    /// <summary>The body written so far.</summary>
    public string GetText() {
        lock (_body) {
            return _writer.ToString() ?? string.Empty;
        }
    }

    /// <summary>Reads or creates a value once, and never after the render was abandoned.</summary>
    /// <param name="slot">Where the created value is kept, so a second read hands out the first.</param>
    /// <param name="create">Creates the value.</param>
    /// <returns>The value, or empty once the render has been abandoned.</returns>
    /// <remarks>
    /// The antiforgery token is the only caller, and it is minted here because the gate is here: a
    /// token minted after the response has gone would leave a cookie nothing can verify.
    /// </remarks>
    public string MintOnce(ref string? slot, Func<string> create) {
        lock (_body) {
            return _abandoned ? string.Empty : slot ??= create();
        }
    }

    /// <summary>Sends what has been written so far and commits the response.</summary>
    /// <param name="response">What the layer asked the response to be.</param>
    public async Task FlushAsync(PageResponse response) {
        CancellationToken deadline = _lifetime?.Token ?? CancellationToken.None;
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(_aborted, deadline);
        CancellationToken cancellationToken = linked.Token;

        await _flushing.WaitAsync(cancellationToken);
        try {
            string text;
            bool first;

            lock (_body) {
                if (_abandoned) {
                    return;
                }

                text = TakeBuffer();
                first = !_commit.Committed;
            }

            // The stream slot is taken, and the render slot released, before anything is committed:
            // a refusal must leave the response whole so the endpoint can still answer with a 503.
            if (first && _lifetime is not null) {
                await _lifetime.BeginStreamAsync();
            }

            lock (_body) {
                if (first) {
                    _commit.Committed = true;
                }
            }

            if (first) {
                await _sink!.StartAsync(response, cancellationToken);
            }

            if (text.Length > 0) {
                await _sink!.WriteAsync(Encoding.UTF8.GetBytes(text), cancellationToken);
            }

            _lifetime?.ReArm();
        } finally {
            _ = _flushing.Release();
        }
    }

    /// <summary>Writes whatever the layer left buffered after its last flush.</summary>
    /// <remarks>
    /// Only a committed response has a body to complete; a buffered one is shaped from the context by
    /// the endpoint. A disconnect or a deadline that fires during this last write is the response
    /// ending, not a page fault.
    /// </remarks>
    public async Task CompleteAsync() {
        if (_sink is null || !_commit.Committed) {
            return;
        }

        CancellationToken deadline = _lifetime?.Token ?? CancellationToken.None;
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(_aborted, deadline);
        CancellationToken cancellationToken = linked.Token;

        await _flushing.WaitAsync(cancellationToken);
        try {
            string text;

            lock (_body) {
                if (_abandoned) {
                    return;
                }

                text = TakeBuffer();
            }

            if (text.Length > 0) {
                await _sink.WriteAsync(Encoding.UTF8.GetBytes(text), cancellationToken);
            }
        } catch (OperationCanceledException) {
            // The response is already committed and the client or the deadline has ended it; there is
            // nothing left to write and nothing to report as a page fault.
        } finally {
            _ = _flushing.Release();
        }
    }

    private string TakeBuffer() {
        StringBuilder buffer = _writer.GetStringBuilder();
        string text = buffer.ToString();
        _ = buffer.Clear();
        return text;
    }
#pragma warning restore CA1001
}
