// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using System.Net;
using System.Text.RegularExpressions;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Pages;
using uk.osric.asp.Web.Tests;

/// <summary>A page that saves its own source.</summary>
[TestFixture]
public sealed partial class PageSelfSaveTests : IntegrationTest {

    // The draft is a fragment, so the fixture runs the host with the stored layout switched on.
    internal override AspApplication CreateApplication() => new(pageLayout: "/_layout");

    // The draft the tests save. It is the smallest page that exercises the bound save: its guard,
    // its own source in a textarea, the head hash the form was drawn from, and the diagnostics the
    // compiler answered with.
    private const string Draft = """
<% ctx.Capabilities.GetRequired<IPageLayout>().Title = "Self-save";
   ctx.Capabilities.GetRequired<IPageComposition>().Wrap(); %>
<h1>Self-save</h1>
<% if (ctx.User is not { } user) { ctx.Response.RedirectToLogin(); return; } %>
<%
    IPageCatalog catalog = ctx.Capabilities.GetRequired<IPageCatalog>();
    PageSource? current = await catalog.ReadAsync(ctx.Path);
    string draft = current?.Source ?? "";
    string headHash = current?.Hash ?? "";
    string? refusal = null;
    PageSaveResult? answered = null;

    if (string.Equals(ctx.Request.Method, "POST", System.StringComparison.Ordinal)) {
        bool hasTemplate = ctx.Request.Form.TryGetValue("template", out string? template);
        bool hasHash = ctx.Request.Form.TryGetValue("hash", out string? expected);

        if (!hasTemplate || template is null) {
            refusal = "That form carried no template, so nothing was saved.";
        } else if (!hasHash || !string.Equals(expected, current?.Hash, System.StringComparison.Ordinal)) {
            refusal = "Somebody saved this page while you were typing, so nothing was saved.";
        } else {
            draft = template;
            answered = await ctx.Capabilities.GetRequired<IPageWrites>().SaveAsync(template);
        }
    }
%>
<% if (refusal is not null) { %>
<p class="notice"><%= refusal %></p>
<% } %>
<% if (answered is not null) { %>
<h2><%= answered.HasErrors ? "Saved with errors" : answered.HasWarnings ? "Saved with warnings" : "Saved" %></h2>
<p>Revision <code><%= answered.Hash %></code> is stored.</p>
<% if (answered.Diagnostics.Count > 0) { %>
<table>
    <caption>What the compiler said about the template you saved</caption>
    <thead>
        <tr>
            <th scope="col">Severity</th>
            <th scope="col">Code</th>
            <th scope="col">Message</th>
        </tr>
    </thead>
    <tbody>
<% foreach (PageDiagnostic diagnostic in answered.Diagnostics) { %>
        <tr>
            <th scope="row"><%= diagnostic.Severity == PageDiagnosticSeverity.Error ? "Error" : "Warning" %></th>
            <td><code><%= diagnostic.Code %></code></td>
            <td><%= diagnostic.Message %></td>
        </tr>
<% } %>
    </tbody>
</table>
<% } %>
<% } %>
<form method="post" class="form">
    <%== ctx.Response.AntiforgeryField %>
    <input type="hidden" name="hash" value="<%= headHash %>">
    <fieldset>
        <legend>This page's own source</legend>
        <textarea name="template" id="template" rows="12" cols="70" aria-describedby="template-hint"><%= draft %></textarea>
        <p id="template-hint">Saving compiles before it stores.</p>
    </fieldset>
    <button type="submit">Save this page</button>
</form>
""";

    [Test]
    public async Task TheDraftSendsAnAnonymousVisitorToSignInAsync() {
        using HttpClient client = CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/self-save");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
        Assert.That(response.Headers.Location?.ToString(), Does.StartWith("/login"));
    }

    [Test]
    public async Task TheDraftShowsItsOwnSourceWhenSignedInAsync() {
        await RestoreDraftAsync();
        using HttpClient client = await CreateSignedInClientAsync();

        using HttpResponseMessage response = await client.GetAsync("/self-save");
        string body = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body, Does.Contain("<h1>Self-save</h1>"));
        Assert.That(body, Does.Contain("name=\"template\""));
        Assert.That(body, Does.Contain("&lt;% if (ctx.User is not { } user)"),
            "the textarea holds the escaped source, not the page's own rendering of it");
    }

    [Test]
    public async Task ASignedInOwnerSavesTheDraftAndTheNextRequestServesItAsync() {
        await RestoreDraftAsync();
        using HttpClient client = await CreateSignedInClientAsync();
        string token = await GetAntiforgeryTokenAsync(client, "/self-save");
        string hash = await HeadHashAsync(client);

        using HttpResponseMessage saved = await client.PostAsync("/self-save", Form(
            ("__RequestVerificationToken", token),
            ("hash", hash),
            ("template", Titled("Saved", "<h1>Saved by the page</h1>"))));
        string answer = await saved.Content.ReadAsStringAsync();

        Assert.That(saved.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(answer, Does.Contain("Saved"));
        Assert.That(answer, Does.Contain((await ReadPageAsync("/self-save"))!.Hash));

        using HttpResponseMessage served = await client.GetAsync("/self-save");

        Assert.That(await served.Content.ReadAsStringAsync(), Does.Contain("<h1>Saved by the page</h1>"));
    }

    [Test]
    public async Task ASaveWithWarningsIsServedOnTheNextRequestAsync() {
        await RestoreDraftAsync();
        using HttpClient client = await CreateSignedInClientAsync();
        string token = await GetAntiforgeryTokenAsync(client, "/self-save");
        string hash = await HeadHashAsync(client);

        using HttpResponseMessage saved = await client.PostAsync("/self-save", Form(
            ("__RequestVerificationToken", token),
            ("hash", hash),
            ("template", Titled("Warned", "<% int unused = 1; %><h1>Warned</h1>"))));

        Assert.That(await saved.Content.ReadAsStringAsync(), Does.Contain("Saved with warnings"));

        using HttpResponseMessage served = await client.GetAsync("/self-save");
        string body = await served.Content.ReadAsStringAsync();

        Assert.That(served.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body, Does.Contain("<h1>Warned</h1>"), "a warning never stops a page");
    }

    [Test]
    public async Task ASaveWithErrorsIsAnsweredWithItsDiagnosticsAsync() {
        await RestoreDraftAsync();
        using HttpClient client = await CreateSignedInClientAsync();
        string token = await GetAntiforgeryTokenAsync(client, "/self-save");
        string hash = await HeadHashAsync(client);

        using HttpResponseMessage saved = await client.PostAsync("/self-save", Form(
            ("__RequestVerificationToken", token),
            ("hash", hash),
            ("template", "<% int value = ; %>")));
        string answer = await saved.Content.ReadAsStringAsync();

        Assert.That(answer, Does.Contain("Saved with errors"));
        Assert.That(answer, Does.Contain("CS"));
        Assert.That((await ReadPageAsync("/self-save"))!.Template, Is.EqualTo("<% int value = ; %>"),
            "an error is stored and reported");

        using HttpResponseMessage served = await client.GetAsync("/self-save");

        Assert.That(served.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
        Assert.That(await served.Content.ReadAsStringAsync(), Does.Contain("Compilation failed"));
    }

    [Test]
    public async Task AnAuthenticatedPostWithoutTheFieldIsRefusedBeforeThePageRunsAsync() {
        string seed = await RestoreDraftAsync();
        using HttpClient client = await CreateSignedInClientAsync();
        string hash = await HeadHashAsync(client);

        using HttpResponseMessage refused = await client.PostAsync("/self-save", Form(
            ("hash", hash),
            ("template", "<h1>Forged</h1>")));

        Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That((await ReadPageAsync("/self-save"))!.Template, Is.EqualTo(seed));
    }

    [Test]
    public async Task AStaleFormIsRefusedAndTheNewerSaveStandsAsync() {
        string seed = await RestoreDraftAsync();
        using HttpClient client = await CreateSignedInClientAsync();
        string staleHash = await HeadHashAsync(client);
        string token = await GetAntiforgeryTokenAsync(client, "/self-save");

        // Somebody saves the page — even the same text — while the form is open: the head moves to a
        // new revision, so the form's hash is stale.
        using HttpResponseMessage newer = await PostEditorAsync(client, "self-save", seed);
        Assert.That(newer.StatusCode, Is.EqualTo(HttpStatusCode.OK), "the editor page answers the save in place");

        using HttpResponseMessage refused = await client.PostAsync("/self-save", Form(
            ("__RequestVerificationToken", token),
            ("hash", staleHash),
            ("template", "<h1>Saved from an old form</h1>")));
        string answer = await refused.Content.ReadAsStringAsync();

        Assert.That(answer, Does.Contain("Somebody saved this page while you were typing"));
        Assert.That((await ReadPageAsync("/self-save"))!.Template, Is.EqualTo(seed), "the newer save stands");
    }

    [Test]
    public async Task AHeadRequestNeverSavesAsync() {
        await RestoreDraftAsync();
        PageTemplate before = (await ReadPageAsync("/self-save"))!;
        using HttpClient client = await CreateSignedInClientAsync();

        using HttpResponseMessage response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/self-save"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await ReadPageAsync("/self-save"))!.Hash, Is.EqualTo(before.Hash));
    }

    [Test]
    public async Task TheDraftIsWrappedInTheStoredLayoutAsync() {
        await RestoreDraftAsync();
        using HttpClient client = await CreateSignedInClientAsync();

        using HttpResponseMessage response = await client.GetAsync("/self-save");
        string body = await response.Content.ReadAsStringAsync();

        Assert.That(body, Does.StartWith("<!DOCTYPE html>"));
        Assert.That(body, Does.Contain("class=\"site-nav\""));
    }

    [Test]
    public async Task TheDraftMeetsTheAccessibilityRulesAsync() {
        await RestoreDraftAsync();
        using HttpClient client = await CreateSignedInClientAsync();

        using HttpResponseMessage response = await client.GetAsync("/self-save");
        MarkupReader document = MarkupReader.Parse(await response.Content.ReadAsStringAsync());

        Assert.That(document.Elements("h1"), Has.Count.EqualTo(1));

        MarkupElement form = document.Elements("form").Single();
        Assert.That(form.Attribute("class"), Is.EqualTo("form"));

        MarkupElement textarea = form.Descendants().Single(element => element.Name == "textarea");
        Assert.That(textarea.Attribute("name"), Is.EqualTo("template"));
        Assert.That(textarea.Attribute("aria-describedby"), Is.Not.Null, "the textarea is described");

        MarkupElement legend = form.Descendants().Single(element => element.Name == "legend");
        Assert.That(legend.Text, Is.Not.Empty, "the fieldset's legend labels the textarea");
    }

    [Test]
    public async Task ASaveWithErrorsListsThemInAScopedTableAsync() {
        await RestoreDraftAsync();
        using HttpClient client = await CreateSignedInClientAsync();
        string token = await GetAntiforgeryTokenAsync(client, "/self-save");
        string hash = await HeadHashAsync(client);

        using HttpResponseMessage saved = await client.PostAsync("/self-save", Form(
            ("__RequestVerificationToken", token),
            ("hash", hash),
            ("template", "<% int value = ; %>")));
        MarkupReader document = MarkupReader.Parse(await saved.Content.ReadAsStringAsync());

        MarkupElement table = document.Elements("table").Single();
        Assert.That(table.Descendants().Where(element => element.Name == "th"), Is.Not.Empty);
        Assert.That(table.Descendants().Where(element => element.Name == "th").All(header => header.HasAttribute("scope")),
            "every heading cell is scoped");
    }

    /// <summary>
    /// A template the wrapped layout can serve: a page saved through this fixture must set a title or the
    /// chain refuses it before the layout runs.
    /// </summary>
    private static string Titled(string title, string body) =>
        $"<% ctx.Capabilities.GetRequired<IPageLayout>().Title = \"{title}\"; %>{body}";

    /// <summary>Puts the draft back, so a fixture that saves does not leave the site changed.</summary>
    private async Task<string> RestoreDraftAsync() {
        await SavePageAsync("/self-save", Draft);

        return Draft;
    }

    /// <summary>The revision hash the draft's form carries, which the page compares before it saves.</summary>
    private static async Task<string> HeadHashAsync(HttpClient client) {
        using HttpResponseMessage response = await client.GetAsync("/self-save");
        string body = await response.Content.ReadAsStringAsync();
        Match match = HashField().Match(body);
        Assert.That(match.Success, Is.True, "the draft rendered no revision hash");

        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    [GeneratedRegex("name=\"hash\" value=\"([^\"]+)\"")]
    private static partial Regex HashField();
}
