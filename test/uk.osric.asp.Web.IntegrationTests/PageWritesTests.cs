// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using System.Net;

/// <summary>A page that is not the editor writing another page, over the real host.</summary>
[TestFixture]
public sealed class PageWritesTests : IntegrationTest {

    [Test]
    public async Task APageCommitsSeveralWritesAsOneTransactionAsync() {
        await SavePageAsync("/target", "<h1>before</h1>");
        await SavePageAsync("/batch", """
            <% if (ctx.User is null) { ctx.Response.RedirectToLogin(); return; } %>
            <% if (ctx.Request.Method == "POST") {
                   await using IPageTransaction batch = ctx.Capabilities.GetRequired<IPageWrites>().BeginTransaction();
                   _ = await batch.SaveAsync("/one", "first", null);
                   _ = await batch.SaveAsync("/two", "second", null);
                   var report = await batch.CommitAsync();
                   foreach (PageWriteResult row in report) {
                       ctx.Response.Write(row.Key + ":" + row.Outcome + " ");
                   }
                   return;
               } %>
            <form method="post" class="form"><%== ctx.Response.AntiforgeryField %>
                <button>Batch</button></form>
            """);

        using HttpClient client = await CreateSignedInClientAsync();
        string token = await GetAntiforgeryTokenAsync(client, "/batch");

        using HttpResponseMessage answer = await client.PostAsync("/batch", Form(("__RequestVerificationToken", token)));
        string answerBody = await answer.Content.ReadAsStringAsync();

        using HttpResponseMessage one = await client.GetAsync("/one");
        using HttpResponseMessage two = await client.GetAsync("/two");

        Assert.Multiple(() => {
            Assert.That(answer.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(answerBody, Does.Contain("/one:Saved"));
            Assert.That(answerBody, Does.Contain("/two:Saved"));
            Assert.That(one.Content.ReadAsStringAsync().Result, Does.Contain("first"));
            Assert.That(two.Content.ReadAsStringAsync().Result, Does.Contain("second"));
        });
    }

    [Test]
    public async Task APageThatIsNotTheEditorSavesAnotherPageAsync() {
        await SavePageAsync("/target", "<h1>before</h1>");
        await SavePageAsync("/writer", """
            <% if (ctx.User is null) { ctx.Response.RedirectToLogin(); return; } %>
            <% ctx.Capabilities.GetRequired<IPageLayout>().Title = "Writer"; %>
            <% IPageCatalog catalog = ctx.Capabilities.GetRequired<IPageCatalog>();
               if (ctx.Request.Method == "POST" && ctx.Request.Form.TryGetValue("template", out string? template) && template is not null) {
                   PageSource? target = await catalog.ReadAsync("/target");
                   PageEditResult saved = await ctx.Capabilities.GetRequired<IPageWrites>()
                       .SaveAsync("/target", template, target?.Hash);
                   ctx.Response.Write("outcome: ");
                   ctx.Response.Write(saved.Outcome.ToString());
                   return;
               } %>
            <form method="post" class="form"><%== ctx.Response.AntiforgeryField %>
                <input name="template" value="&lt;h1&gt;after&lt;/h1&gt;">
                <button>Save</button></form>
            """);

        using HttpClient client = await CreateSignedInClientAsync();
        string token = await GetAntiforgeryTokenAsync(client, "/writer");

        using HttpResponseMessage answer = await client.PostAsync("/writer", Form(
            ("__RequestVerificationToken", token),
            ("template", "<h1>after</h1>")));
        string answerBody = await answer.Content.ReadAsStringAsync();
        using HttpResponseMessage served = await client.GetAsync("/target");
        string servedBody = await served.Content.ReadAsStringAsync();

        Assert.Multiple(() => {
            Assert.That(answer.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(answerBody, Does.Contain("outcome: Saved"), "the page answers with the save it made");
            Assert.That(servedBody, Does.Contain("<h1>after</h1>"), "and the next request serves the other page");
        });
    }
}
