// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using System.Net;

/// <summary>A page moved by the editor, over the real host.</summary>
[TestFixture]
public sealed class PageMoveTests : IntegrationTest {

    [Test]
    public async Task TheEditorMovesAPageAndTheOldKeyAnswersNotFoundAsync() {
        await SavePageAsync("/target", "<h1>before</h1>");
        using HttpClient client = await CreateSignedInClientAsync();

        const string Url = "/pages?edit=%2Ftarget&cmd=move";
        (string token, string revision) = await GetEditorFormAsync(client, "/pages?edit=%2Ftarget&cmd=move");
        using HttpResponseMessage moved = await client.PostAsync(Url,
            Form(("__RequestVerificationToken", token), ("expected", revision), ("to", "/moved")));
        string answer = await moved.Content.ReadAsStringAsync();
        using HttpResponseMessage served = await client.GetAsync("/moved");
        string servedBody = await served.Content.ReadAsStringAsync();
        using HttpResponseMessage old = await client.GetAsync("/target");

        Assert.Multiple(() => {
            Assert.That(moved.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(answer, Does.Contain("Moved /target to /moved"));
            Assert.That(served.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(servedBody, Does.Contain("<h1>before</h1>"),
                "the next request serves the page at its new key");
            Assert.That(old.StatusCode, Is.EqualTo(HttpStatusCode.NotFound),
                "nothing is left at the old key");
        });
    }
}
