// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace AspLanguageServer.Tests;

using System.Text.Json.Nodes;

/// <summary>A transport a test writes the editor's side of.</summary>
/// <remarks>
/// The server reads until the queue is empty and then sees the end of the stream, so a test sends its
/// whole script and one call runs the loop to the end.
/// </remarks>
internal sealed class FakeTransport : ILspTransport {

    private readonly Queue<JsonObject> _incoming = [];

    /// <summary>Every message the server has written, in order.</summary>
    public List<JsonObject> Written { get; } = [];

    /// <summary>Queues a message for the server to read.</summary>
    /// <param name="message">The message, as the editor would send it.</param>
    public void Send(JsonObject message) => _incoming.Enqueue(message);

    /// <inheritdoc/>
    public Task<JsonObject?> ReadAsync(CancellationToken cancellationToken) =>
        Task.FromResult(_incoming.Count == 0 ? null : _incoming.Dequeue());

    /// <inheritdoc/>
    public Task WriteAsync(JsonObject message, CancellationToken cancellationToken) {
        Written.Add(message);
        return Task.CompletedTask;
    }
}
