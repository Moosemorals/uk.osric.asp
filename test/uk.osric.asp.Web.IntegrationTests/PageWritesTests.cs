// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using System.Net;

/// <summary>A page that is not the editor writing another page, over the real host.</summary>
[TestFixture]
public sealed class PageWritesTests : IntegrationTest {

    [Test]
    public async Task APageThatIsNotTheEditorSavesAnotherPageAsync() {
        await SavePageAsync("/target", "<h1>before</h1>");
        await SavePageAsync("/writer", """
            <% if (ctx.User is null) { ctx.RedirectToLogin(); return; } %>
            <% ctx.Capabilities.GetRequired<IPageLayout>().Title = "Writer"; %>
            <% IPageCatalog catalog = ctx.Capabilities.GetRequired<IPageCatalog>();
               if (ctx.Request.Method == "POST" && ctx.Request.Form.TryGetValue("template", out string? template) && template is not null) {
                   PageSource? target = await catalog.ReadAsync("/target");
                   PageEditResult saved = await ctx.Capabilities.GetRequired<IPageWrites>()
                       .SaveAsync("/target", template, target?.Hash);
                   ctx.Write("outcome: ");
                   ctx.Write(saved.Outcome.ToString());
                   return;
               } %>
            <form method="post" class="form"><%== ctx.AntiforgeryField %>
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
