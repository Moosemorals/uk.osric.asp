// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using System.Net;
using uk.osric.asp.Web.Pages;

/// <summary>A stored page asking whether a source would check, through the real host.</summary>
[TestFixture]
public sealed class PageCheckTests : IntegrationTest {

    // The page reports whether it was offered a check at all, so the same page proves the gate and
    // the answer: an anonymous render is offered none and still serves.
    private const string Checker = """
<% IPageCheck? check = ctx.Capabilities.Get<IPageCheck>(); %>
<p id="offered"><%= check is null ? "no" : "yes" %></p>
<% if (check is not null) {
       PageCheckResult result = await check.CheckAsync("template", "<% int broken = ; %\u003e");
       int errors = 0;
       foreach (PageDiagnostic diagnostic in result.Diagnostics) {
           if (diagnostic.Severity == PageDiagnosticSeverity.Error) { errors++; }
       } %>
<p id="valid"><%= result.Valid ? "valid" : "invalid" %></p>
<p id="kind"><%= result.Kind?.ToString() ?? "none" %></p>
<p id="errors"><%= errors.ToString() %></p>
<% } %>
""";

    private const string CheckerPath = "/check-without-saving";

    [Test]
    public async Task ASignedInVisitorIsOfferedACheckAndGetsItsAnswerAsync() {
        await SavePageAsync(CheckerPath, Checker);
        using HttpClient client = await CreateSignedInClientAsync();

        HttpResponseMessage response = await client.GetAsync(CheckerPath);
        string body = await response.Content.ReadAsStringAsync();
        Assert.That(response.IsSuccessStatusCode, Is.True, body);

        Assert.Multiple(() => {
            Assert.That(body, Does.Contain("id=\"offered\">yes"));
            Assert.That(body, Does.Contain("id=\"valid\">invalid"));
            Assert.That(body, Does.Contain("id=\"kind\">Page"));
            Assert.That(body, Does.Contain("id=\"errors\">1"));
        });
    }

    [Test]
    public async Task AnAnonymousVisitorIsNotOfferedACheckAsync() {
        await SavePageAsync(CheckerPath, Checker);
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync(CheckerPath);
        string body = await response.Content.ReadAsStringAsync();

        Assert.Multiple(() => {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body, Does.Contain("id=\"offered\">no"));
        });
    }

    [Test]
    public async Task ACheckLeavesTheCallingPagesHeadWhereItWasAsync() {
        await SavePageAsync(CheckerPath, Checker);
        PageTemplate? before = await ReadPageAsync(CheckerPath);
        using HttpClient client = await CreateSignedInClientAsync();

        _ = await client.GetStringAsync(CheckerPath);

        PageTemplate? after = await ReadPageAsync(CheckerPath);
        Assert.That(after?.Hash, Is.EqualTo(before?.Hash), "a check stores no revision");
    }
}
