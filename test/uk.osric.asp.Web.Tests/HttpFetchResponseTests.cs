// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Sdk;

/// <summary>The Sdk's fetch outcome, asserted from the suite that drives it.</summary>
/// <remarks>
/// The Sdk has no suite of its own. <c>Succeeded</c> is the one predicate a page branches on, so the
/// boundaries either side of 2xx and the transport failure that keeps a status are pinned here.
/// </remarks>
[TestFixture]
public sealed class HttpFetchResponseTests {

    [TestCase(200)]
    [TestCase(204)]
    [TestCase(299)]
    public void ATwoHundredWithoutAFailureSucceeded(int status) {
        Assert.That(Response(status, HttpFetchFailure.None).Succeeded, Is.True);
    }

    [TestCase(199)]
    [TestCase(300)]
    [TestCase(404)]
    [TestCase(500)]
    public void AStatusOutsideTwoHundredDidNotSucceed(int status) {
        Assert.That(Response(status, HttpFetchFailure.None).Succeeded, Is.False);
    }

    [TestCase(HttpFetchFailure.Network)]
    [TestCase(HttpFetchFailure.Timeout)]
    [TestCase(HttpFetchFailure.InvalidRequest)]
    [TestCase(HttpFetchFailure.TooManyRedirects)]
    [TestCase(HttpFetchFailure.Aborted)]
    public void ATransportFailureDidNotSucceedWhateverTheStatus(HttpFetchFailure failure) {
        Assert.That(Response(200, failure).Succeeded, Is.False);
    }

    private static HttpFetchResponse Response(int status, HttpFetchFailure failure) =>
        new(status, new Dictionary<string, string>(), string.Empty, failure, Truncated: false);
}
