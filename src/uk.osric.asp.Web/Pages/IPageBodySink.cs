// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Sdk;

/// <summary>The response a streaming page writes into.</summary>
/// <remarks>
/// The first <see cref="StartAsync"/> is the commit point: headers are final from then on and the body
/// is sent as it is written.
/// </remarks>
internal interface IPageBodySink {

    /// <summary>Applies and starts the response, making the headers final.</summary>
    /// <param name="response">What the page asked the response to be.</param>
    /// <param name="cancellationToken">Cancels the start.</param>
    public Task StartAsync(PageResponse response, CancellationToken cancellationToken);

    /// <summary>Writes one flushed piece of the body.</summary>
    /// <param name="bytes">The UTF-8 body to write.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    public Task WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken);
}
