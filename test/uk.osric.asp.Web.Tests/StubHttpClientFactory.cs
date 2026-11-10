// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

/// <summary>An <see cref="IHttpClientFactory"/> over one handler, for tests with no socket.</summary>
internal sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory {
    // A fresh client per call, because the fetcher disposes the one it is given.
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}
