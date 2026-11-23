// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

#pragma warning disable CA1711 // The page contract deliberately names the line-oriented response type a stream.
/// <summary>A bounded, line-oriented text response from an outbound HTTP request.</summary>
/// <remarks>
/// A null line means the response ended. <see cref="Failure"/> and <see cref="Truncated"/> explain
/// whether it ended normally or was cut short. The stream occupies the same bounded outbound-fetch
/// budget as a buffered request and must be closed when it is no longer needed.
/// </remarks>
public abstract class HttpFetchStream {

    /// <summary>Initializes a stream implementation.</summary>
    protected HttpFetchStream() { }

    /// <summary>The HTTP status, or zero when no response was received.</summary>
    public abstract int Status { get; }

    /// <summary>The response headers exposed by the host.</summary>
    public abstract IReadOnlyDictionary<string, string> Headers { get; }

    /// <summary>Why the request or stream ended, or <see cref="HttpFetchFailure.None"/>.</summary>
    public abstract HttpFetchFailure Failure { get; }

    /// <summary>Whether a timeout, transport failure or configured byte limit ended the body early.</summary>
    public abstract bool Truncated { get; }

    /// <summary>Reads the next decoded line, or returns <see langword="null"/> when the stream has ended.</summary>
    /// <returns>The next line without its line terminator, or <see langword="null"/> at end of stream.</returns>
    public abstract Task<string?> ReadLineAsync();

    /// <summary>Closes the response and releases its connection.</summary>
    /// <returns>A task that completes when the response is closed.</returns>
    public abstract Task CloseAsync();
}
#pragma warning restore CA1711
