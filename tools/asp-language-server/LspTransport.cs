// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace AspLanguageServer;

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>One Language Server Protocol message, read or written.</summary>
/// <remarks>
/// The transport is behind an interface so the protocol suite drives the server with messages rather
/// than with a process: the framing is proven once, over streams, and everything above it is a value.
/// </remarks>
internal interface ILspTransport {

    /// <summary>Reads the next message.</summary>
    /// <param name="cancellationToken">Stops the read.</param>
    /// <returns>The message, or <see langword="null"/> at the end of the stream.</returns>
    Task<JsonObject?> ReadAsync(CancellationToken cancellationToken);

    /// <summary>Writes one message.</summary>
    /// <param name="message">The message to frame and send.</param>
    /// <param name="cancellationToken">Stops the write.</param>
    Task WriteAsync(JsonObject message, CancellationToken cancellationToken);
}

/// <summary>Content-Length framed JSON-RPC over a pair of streams, which is what stdio is.</summary>
internal sealed class StreamTransport(Stream input, Stream output) : ILspTransport {

    /// <inheritdoc/>
    public async Task<JsonObject?> ReadAsync(CancellationToken cancellationToken) {
        int length = -1;

        while (true) {
            string? header = await ReadHeaderLineAsync(input, cancellationToken);
            if (header is null) {
                // The client closed the pipe, which is how a server learns its editor has gone.
                return null;
            }

            if (header.Length == 0) {
                break;
            }

            int colon = header.IndexOf(':', StringComparison.Ordinal);
            if (colon > 0
                && header[..colon].Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) {
                length = int.Parse(header[(colon + 1)..].Trim(), CultureInfo.InvariantCulture);
            }
        }

        if (length < 0) {
            throw new InvalidDataException("An LSP message carries no Content-Length header.");
        }

        byte[] body = new byte[length];
        await input.ReadExactlyAsync(body, cancellationToken);

        return JsonNode.Parse(Encoding.UTF8.GetString(body)) as JsonObject
            ?? throw new InvalidDataException("An LSP message is not a JSON object.");
    }

    /// <inheritdoc/>
    public async Task WriteAsync(JsonObject message, CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(message);

        // Content-Length counts bytes, not characters: a diagnostic message with an accent in it is
        // longer as UTF-8 than as text, and a header that disagreed would desynchronise the stream.
        byte[] body = Encoding.UTF8.GetBytes(message.ToJsonString());
        byte[] header = Encoding.ASCII.GetBytes(
            string.Create(CultureInfo.InvariantCulture, $"Content-Length: {body.Length}\r\n\r\n"));

        await output.WriteAsync(header, cancellationToken);
        await output.WriteAsync(body, cancellationToken);
        await output.FlushAsync(cancellationToken);
    }

    // The header block is ASCII and short: one byte at a time is the whole of it, and a buffered
    // reader would swallow the first bytes of the body.
    private static async Task<string?> ReadHeaderLineAsync(Stream input, CancellationToken cancellationToken) {
        List<byte> line = [];
        byte[] one = new byte[1];

        while (true) {
            int read = await input.ReadAsync(one, cancellationToken);
            if (read == 0) {
                return line.Count == 0 ? null : Encoding.ASCII.GetString([.. line]);
            }

            if (one[0] == (byte)'\n') {
                return Encoding.ASCII.GetString([.. line]).TrimEnd('\r');
            }

            line.Add(one[0]);
        }
    }
}
