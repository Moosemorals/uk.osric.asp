// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace AspLanguageServer.Tests;

using System.Text;
using System.Text.Json.Nodes;

/// <summary>The framing: Content-Length over a stream, which is the whole of the transport.</summary>
[TestFixture]
public sealed class StreamTransportTests {

    [Test]
    public async Task AMessageRoundTripsThroughTheFramingAsync() {
        JsonObject message = new() {
            ["jsonrpc"] = "2.0",
            ["method"] = "textDocument/publishDiagnostics",
            ["params"] = new JsonObject {
                // Not ASCII: Content-Length counts bytes, and a header that counted characters would
                // desynchronise the stream on the first accented diagnostic.
                ["message"] = "the café is closed",
            },
        };
        using MemoryStream pipe = new();
        StreamTransport writer = new(pipe, pipe);

        await writer.WriteAsync(message, CancellationToken.None);

        string framed = Encoding.UTF8.GetString(pipe.ToArray());
        byte[] body = Encoding.UTF8.GetBytes(message.ToJsonString());
        Assert.That(framed, Does.StartWith($"Content-Length: {body.Length}\r\n\r\n"));

        pipe.Position = 0;
        JsonObject? read = await new StreamTransport(pipe, pipe).ReadAsync(CancellationToken.None);

        Assert.That(read?.ToJsonString(), Is.EqualTo(message.ToJsonString()));
    }

    [Test]
    public async Task TheEndOfTheStreamReadsAsNothingAsync() {
        using MemoryStream empty = new();

        JsonObject? message = await new StreamTransport(empty, empty).ReadAsync(CancellationToken.None);

        Assert.That(message, Is.Null);
    }

    [Test]
    public async Task AMessageWithNoContentLengthIsRefusedAsync() {
        using MemoryStream stream = new(Encoding.ASCII.GetBytes("Content-Type: application/vscode-jsonrpc\r\n\r\n{}"));

        _ = await Assert.ThrowsAsync<InvalidDataException>(
            () => new StreamTransport(stream, stream).ReadAsync(CancellationToken.None));
    }
}
