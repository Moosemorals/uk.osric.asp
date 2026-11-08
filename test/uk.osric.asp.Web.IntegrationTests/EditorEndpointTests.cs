// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using System.Net;
using uk.osric.asp.Web.Pages;

[TestFixture]
public sealed class EditorEndpointTests : IntegrationTest {

    [Test]
    public async Task BareEditorPathListsPagesWhileTrailingSlashEditsTheRoot() {
        await SavePageAsync("/", "Root source.");
        using HttpClient client = await CreateSignedInClientAsync();

        HttpResponseMessage listing = await client.GetAsync("/edit");
        HttpResponseMessage rootEditor = await client.GetAsync("/edit/");

        string listingBody = await listing.Content.ReadAsStringAsync();
        string editorBody = await rootEditor.Content.ReadAsStringAsync();
        Assert.That(listing.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(listing.Headers.CacheControl?.NoStore, Is.True);
        Assert.That(listingBody, Does.Not.Contain("name=\"template\""));
        Assert.That(rootEditor.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(editorBody, Does.Contain(">\nRoot source.</textarea>"));
    }

    [TestCase("delete")]
    [TestCase("scrub")]
    public async Task CommandNamedPageKeysRemainOrdinaryEditors(string key) {
        await SavePageAsync("/" + key, key + " source.");
        using HttpClient client = await CreateSignedInClientAsync();

        HttpResponseMessage response = await client.GetAsync("/edit/" + key);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain(">\n" + key + " source.</textarea>"));
    }

    [Test]
    public async Task NewPageCommandRedirectsToCanonicalEditorWithoutSaving() {
        using HttpClient client = await CreateSignedInClientAsync();
        string token = await GetAntiforgeryTokenAsync(client, "/edit");

        HttpResponseMessage response = await client.PostAsync("/edit?cmd=new", Form(
            ("__RequestVerificationToken", token),
            ("path", "New//Page")));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.SeeOther));
        Assert.That(response.Headers.Location?.ToString(), Is.EqualTo("/edit/new/page"));
        Assert.That((await client.GetAsync("/new/page")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task NewPageCommandRequiresAntiforgeryAndDoesNotCacheRejection() {
        using HttpClient client = await CreateSignedInClientAsync();

        HttpResponseMessage response = await client.PostAsync("/edit?cmd=new", Form(("path", "unprotected")));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
        Assert.That((await client.GetAsync("/unprotected")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task HistoryCommandOnRootUsesTrailingSlash() {
        await SavePageAsync("/", "Root template.");
        using HttpClient client = await CreateSignedInClientAsync();

        HttpResponseMessage response = await client.GetAsync("/edit/?cmd=history");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("History for /"));
    }

    [Test]
    public async Task UnknownAndRepeatedCommandsReturnNoStoreBadRequest() {
        using HttpClient client = await CreateSignedInClientAsync();

        HttpResponseMessage unknown = await client.GetAsync("/edit/missing?cmd=unknown");
        HttpResponseMessage repeated = await client.GetAsync("/edit/missing?cmd=delete&cmd=scrub");

        Assert.That(unknown.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(unknown.Headers.CacheControl?.NoStore, Is.True);
        Assert.That(repeated.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(repeated.Headers.CacheControl?.NoStore, Is.True);
    }

    [Test]
    public async Task GeneratedCommandIsRefusedAsync() {
        await SavePageAsync("/inspect", "<% ctx.Write(\"ok\"); %>");
        PageTemplate before = (await ReadPageAsync("/inspect"))!;
        using HttpClient client = await CreateSignedInClientAsync();
        int compiles = CompileCount;

        HttpResponseMessage response = await client.GetAsync("/edit/inspect?cmd=generated");

        PageTemplate after = (await ReadPageAsync("/inspect"))!;
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
        Assert.That(CompileCount, Is.EqualTo(compiles));
        Assert.That(after.Hash, Is.EqualTo(before.Hash));
    }

    [Test]
    public async Task HistoryListsAndShowsAnOlderRevisionWithoutCompilingItAsync() {
        using HttpClient client = await CreateSignedInClientAsync();
        await PostEditorAsync(client, "history-page", "first <version>");
        PageTemplate first = (await ReadPageAsync("/history-page"))!;
        await PostEditorAsync(client, "history-page", "second version");
        int compiles = CompileCount;

        HttpResponseMessage history = await client.GetAsync("/edit/history-page?cmd=history");
        string historyBody = await history.Content.ReadAsStringAsync();
        HttpResponseMessage revision = await client.GetAsync($"/edit/history-page?cmd=revision&hash={first.Hash}");
        string revisionBody = await revision.Content.ReadAsStringAsync();

        Assert.That(history.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(history.Headers.CacheControl?.NoStore, Is.True);
        Assert.That(historyBody, Does.Contain(first.Hash));
        Assert.That(historyBody, Does.Contain("<strong>Current</strong>"));
        Assert.That(historyBody, Does.Contain("Compiled"));
        Assert.That(revision.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(revisionBody, Does.Contain("first &lt;version&gt;"));
        Assert.That(revisionBody, Does.Not.Contain("second version"));
        Assert.That(revision.Headers.CacheControl?.NoStore, Is.True);
        Assert.That(CompileCount, Is.EqualTo(compiles));
    }

    [Test]
    public async Task RevisionViewerRejectsMalformedAndOtherPageHashesAsync() {
        await SavePageAsync("/first-page", "first");
        await SavePageAsync("/second-page", "second");
        PageTemplate first = (await ReadPageAsync("/first-page"))!;
        using HttpClient client = await CreateSignedInClientAsync();

        HttpResponseMessage malformed = await client.GetAsync("/edit/first-page?cmd=revision&hash=bad");
        HttpResponseMessage otherPath = await client.GetAsync($"/edit/second-page?cmd=revision&hash={first.Hash}");

        Assert.That(malformed.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(otherPath.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(otherPath.Headers.CacheControl?.NoStore, Is.True);
    }

    [Test]
    public async Task DeleteConfirmationIsReadOnlyAndDeleteRedirectsToListing() {
        await SavePageAsync("/archive", "retained source");
        using HttpClient client = await CreateSignedInClientAsync();

        HttpResponseMessage confirmation = await client.GetAsync("/edit/archive?cmd=delete");
        string confirmationBody = await confirmation.Content.ReadAsStringAsync();
        string token = await GetAntiforgeryTokenAsync(client, "/edit/archive?cmd=delete");
        HttpResponseMessage deleted = await client.PostAsync("/edit/archive?cmd=delete", Form(("__RequestVerificationToken", token)));

        Assert.That(confirmation.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(confirmationBody, Does.Contain("retains its template and revision history"));
        Assert.That(deleted.StatusCode, Is.EqualTo(HttpStatusCode.SeeOther));
        Assert.That(deleted.Headers.Location?.ToString(), Is.EqualTo("/edit"));
        Assert.That((await client.GetAsync("/archive")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(await (await client.GetAsync("/edit")).Content.ReadAsStringAsync(), Does.Contain("/archive (history retained)"));
        HttpResponseMessage history = await client.GetAsync("/edit/archive?cmd=history");
        Assert.That(history.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await history.Content.ReadAsStringAsync(), Does.Contain("This page is deleted. Its revisions remain available here."));
    }

    [Test]
    public async Task SaveAndViewSavesTheSourceAndLandsOnThePageAsync() {
        await SavePageAsync("/hello", "Hello.");
        using HttpClient client = await CreateSignedInClientAsync();
        string token = await GetAntiforgeryTokenAsync(client, "/edit/hello");

        HttpResponseMessage save = await client.PostAsync("/edit/hello", Form(
            ("__RequestVerificationToken", token),
            ("template", "Hello, world!"),
            ("cmd", "save-and-view")));

        Assert.That(save.StatusCode, Is.EqualTo(HttpStatusCode.SeeOther));
        Assert.That(save.Headers.Location?.ToString(), Is.EqualTo("/hello"));

        using HttpResponseMessage page = await client.GetAsync("/hello");
        Assert.That(await page.Content.ReadAsStringAsync(), Is.EqualTo("Hello, world!"));
    }

    [Test]
    public async Task SaveAndViewKeepsTheAuthorOnTheEditorWhenThePageDoesNotCompileAsync() {
        await SavePageAsync("/broken", "fine");
        using HttpClient client = await CreateSignedInClientAsync();
        string token = await GetAntiforgeryTokenAsync(client, "/edit/broken");

        HttpResponseMessage save = await client.PostAsync("/edit/broken", Form(
            ("__RequestVerificationToken", token),
            ("template", "<% int value = ; %>"),
            ("cmd", "save-and-view")));

        Assert.That(save.StatusCode, Is.EqualTo(HttpStatusCode.SeeOther));
        Assert.That(save.Headers.Location?.ToString(), Is.EqualTo("/edit/broken"));

        string editor = await (await client.GetAsync("/edit/broken")).Content.ReadAsStringAsync();
        Assert.That(editor, Does.Contain("The page cannot be shown until the errors are fixed."));
    }

    [Test]
    public async Task ScrubRequiresExactTypedPathAndRemovesThePage() {
        await SavePageAsync("/private", "private source");
        using HttpClient client = await CreateSignedInClientAsync();

        HttpResponseMessage liveConfirmation = await client.GetAsync("/edit/private?cmd=scrub");
        string token = await GetAntiforgeryTokenAsync(client, "/edit/private");
        HttpResponseMessage liveScrub = await client.PostAsync("/edit/private?cmd=scrub", Form(
            ("__RequestVerificationToken", token),
            ("confirmation", "/private")));

        Assert.That(liveConfirmation.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(await liveConfirmation.Content.ReadAsStringAsync(), Does.Contain("Delete the page before scrubbing its history"));
        Assert.That(liveScrub.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(await ReadPageAsync("/private"), Is.Not.Null);

        token = await GetAntiforgeryTokenAsync(client, "/edit/private?cmd=delete");
        HttpResponseMessage deleted = await client.PostAsync("/edit/private?cmd=delete", Form(("__RequestVerificationToken", token)));
        Assert.That(deleted.StatusCode, Is.EqualTo(HttpStatusCode.SeeOther));
        Assert.That(await (await client.GetAsync("/edit/private?cmd=history")).Content.ReadAsStringAsync(),
            Does.Contain("This page is deleted. Its revisions remain available here."));

        token = await GetAntiforgeryTokenAsync(client, "/edit/private?cmd=scrub");
        HttpResponseMessage wrongConfirmation = await client.PostAsync("/edit/private?cmd=scrub", Form(
            ("__RequestVerificationToken", token),
            ("confirmation", "/PRIVATE")));

        Assert.That(wrongConfirmation.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await (await client.GetAsync("/edit/private?cmd=history")).Content.ReadAsStringAsync(),
            Does.Contain("This page is deleted. Its revisions remain available here."));

        token = await GetAntiforgeryTokenAsync(client, "/edit/private?cmd=scrub");
        HttpResponseMessage scrubbed = await client.PostAsync("/edit/private?cmd=scrub", Form(
            ("__RequestVerificationToken", token),
            ("confirmation", "/private")));

        Assert.That(scrubbed.StatusCode, Is.EqualTo(HttpStatusCode.SeeOther));
        Assert.That(scrubbed.Headers.Location?.ToString(), Is.EqualTo("/edit"));
        Assert.That(await ReadPageAsync("/private"), Is.Null);
        Assert.That((await client.GetAsync("/private")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task CanonicalRedirectPreservesEditorCommand() {
        using HttpClient client = await CreateSignedInClientAsync();

        HttpResponseMessage response = await client.GetAsync("/edit/Hello?cmd=history");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.PermanentRedirect));
        Assert.That(response.Headers.Location?.ToString(), Is.EqualTo("/edit/hello?cmd=history"));
        Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
    }

    [TestCase("/space name", "/space%20name")]
    [TestCase("/literal%2fvalue", "/literal%252fvalue")]
    [TestCase("/a&b", "/a%26b")]
    public async Task EncodedEditorLinksRoundTripToTheSamePage(string key, string url) {
        await SavePageAsync(key, "literal path source");
        using HttpClient client = await CreateSignedInClientAsync();

        HttpResponseMessage editor = await client.GetAsync("/edit" + url);
        HttpResponseMessage history = await client.GetAsync("/edit" + url + "?cmd=history");

        Assert.That(editor.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await editor.Content.ReadAsStringAsync(), Does.Contain(">\nliteral path source</textarea>"));
        Assert.That(history.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await history.Content.ReadAsStringAsync(), Does.Contain("History for "));
    }

    [Test]
    public async Task ScrubCannotBeConfirmedAgainAfterTheHeadIsGone() {
        await SavePageAsync("/retry-scrub", "secret");
        using HttpClient client = await CreateSignedInClientAsync();
        string token = await GetAntiforgeryTokenAsync(client, "/edit/retry-scrub?cmd=delete");
        await client.PostAsync("/edit/retry-scrub?cmd=delete", Form(("__RequestVerificationToken", token)));
        token = await GetAntiforgeryTokenAsync(client, "/edit/retry-scrub?cmd=scrub");
        await client.PostAsync("/edit/retry-scrub?cmd=scrub", Form(
            ("__RequestVerificationToken", token), ("confirmation", "/retry-scrub")));

        token = await GetAntiforgeryTokenAsync(client, "/edit/retry-scrub");
        HttpResponseMessage retry = await client.PostAsync("/edit/retry-scrub?cmd=scrub", Form(
            ("__RequestVerificationToken", token), ("confirmation", "/retry-scrub")));

        Assert.That(retry.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(await ReadPageAsync("/retry-scrub"), Is.Null);
    }

    [Test]
    public async Task EditorRendersTheStoredSource() {
        await SavePageAsync("/hello", "Hello, world!");
        using HttpClient client = await CreateSignedInClientAsync();

        HttpResponseMessage response = await client.GetAsync("/edit/hello");

        string body = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/html"));
        Assert.That(body, Does.Contain("name=\"template\""));
        Assert.That(body, Does.Contain(">\nHello, world!</textarea>"));
        Assert.That(body, Does.Contain("?cmd=history"));
        Assert.That(body, Does.Contain(
            "<nav class=\"editor-menu\" aria-label=\"Editor navigation\"><a href=\"/edit\">Page list</a>"
            + " <a href=\"/hello\">View file</a>"
            + " <a href=\"/edit/hello?cmd=history\">History</a>"
            + " <a href=\"/edit/hello?cmd=delete\">Delete</a>"
            + " <a href=\"/help\">Help</a></nav>"));
        Assert.That(body, Does.Contain("<button type=\"submit\" name=\"cmd\" value=\"save\">Save</button>"));
        Assert.That(body, Does.Contain("<button type=\"submit\" name=\"cmd\" value=\"save-and-view\">Save and view</button>"));
        Assert.That(body, Does.Not.Contain("?cmd=scrub"));
        Assert.That(body, Does.Contain("name=\"__RequestVerificationToken\""));
        Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
    }

    [Test]
    public async Task EditorRendersAnEmptyFormForANewPage() {
        using HttpClient client = await CreateSignedInClientAsync();

        HttpResponseMessage response = await client.GetAsync("/edit/new-page");

        string body = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body, Does.Contain("name=\"template\""));
        Assert.That(body, Does.Contain(">\n</textarea>"));
    }

    [Test]
    public async Task ASourceBeginningWithANewlineRoundTripsThroughASave() {
        using HttpClient client = await CreateSignedInClientAsync();

        using HttpResponseMessage save = await PostEditorAsync(client, "leading-newline", "\nHello, world!");
        Assert.That(save.StatusCode, Is.EqualTo(HttpStatusCode.SeeOther));

        HttpResponseMessage response = await client.GetAsync("/edit/leading-newline");
        string body = await response.Content.ReadAsStringAsync();

        // The newline the marker emits is the one the HTML parser drops, so the author's survives.
        Assert.That(body, Does.Contain(">\n\nHello, world!</textarea>"));
        Assert.That(body, Does.Contain("<span id=\"L1\">1</span><span id=\"L2\">2</span>"));
    }

    [Test]
    public async Task SaveRedirectsToTheEditorAndStoresTheSource() {
        using HttpClient client = await CreateSignedInClientAsync();

        HttpResponseMessage save = await PostEditorAsync(client, "hello", "Saved, <% ctx.Write(\"world\"); %>!");

        Assert.That(save.StatusCode, Is.EqualTo(HttpStatusCode.SeeOther));
        Assert.That(save.Headers.Location?.ToString(), Is.EqualTo("/edit/hello"));

        HttpResponseMessage page = await client.GetAsync("/hello");
        Assert.That(page.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await page.Content.ReadAsStringAsync(), Is.EqualTo("Saved, world!"));
    }

    [Test]
    public async Task SaveWithoutATokenReturns400AndWritesNothing() {
        using HttpClient client = await CreateSignedInClientAsync();

        HttpResponseMessage save = await client.PostAsync("/edit/no-token", Form(("template", "hijacked")));

        Assert.That(save.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        HttpResponseMessage page = await client.GetAsync("/no-token");
        Assert.That(page.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task SavingAWarningOnlyPageReportsAWarningAndServesIt() {
        using HttpClient client = await CreateSignedInClientAsync();
        int compiles = CompileCount;

        HttpResponseMessage save = await PostEditorAsync(client, "warned", "<% int unused = 1; %>");
        Assert.That(save.StatusCode, Is.EqualTo(HttpStatusCode.SeeOther));

        string editor = await (await client.GetAsync("/edit/warned")).Content.ReadAsStringAsync();
        Assert.That(editor, Does.Contain("Saved with warnings."));
        Assert.That(editor, Does.Contain("warning CS0219"));

        HttpResponseMessage page = await client.GetAsync("/warned");
        Assert.That(page.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await page.Content.ReadAsStringAsync(), Is.Empty);
        Assert.That(CompileCount - compiles, Is.EqualTo(1), "the save compiled the page; the view must not");
    }

    [Test]
    public async Task SavedMessageIsShownOnTheNextRequestOnly() {
        using HttpClient client = await CreateSignedInClientAsync();

        HttpResponseMessage save = await PostEditorAsync(client, "flash", "Hello.");
        Assert.That(save.StatusCode, Is.EqualTo(HttpStatusCode.SeeOther));

        HttpResponseMessage confirmation = await client.GetAsync("/edit/flash");
        Assert.That(await confirmation.Content.ReadAsStringAsync(), Does.Contain("<p>Saved successfully.</p>"));

        HttpResponseMessage next = await client.GetAsync("/edit/flash");
        Assert.That(await next.Content.ReadAsStringAsync(), Does.Not.Contain("Saved successfully."));
    }

    [Test]
    public async Task SaveWithoutContentReportsValidationAndWritesNothing() {
        using HttpClient client = await CreateSignedInClientAsync();

        HttpResponseMessage save = await PostEditorAsync(client, "required", null);
        Assert.That(save.StatusCode, Is.EqualTo(HttpStatusCode.SeeOther));

        HttpResponseMessage editor = await client.GetAsync("/edit/required");
        Assert.That(await editor.Content.ReadAsStringAsync(), Does.Contain("A template is required."));

        HttpResponseMessage page = await client.GetAsync("/required");
        Assert.That(page.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task SavingAReservedKeyIsRefused() {
        using HttpClient client = await CreateSignedInClientAsync();

        HttpResponseMessage save = await PostEditorAsync(client, "favicon.ico", "hijacked");
        Assert.That(save.StatusCode, Is.EqualTo(HttpStatusCode.SeeOther));

        HttpResponseMessage editor = await client.GetAsync("/edit/favicon.ico");
        Assert.That(await editor.Content.ReadAsStringAsync(), Does.Contain("/favicon.ico is reserved by the site."));

        HttpResponseMessage page = await client.GetAsync("/favicon.ico");
        Assert.That(page.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
