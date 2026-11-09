// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.BrowserTests;

/// <summary>A stored page may load a script it serves from the same origin.</summary>
[TestFixture]
internal sealed class PageScriptTests : BrowserTest {

    [Test]
    public async Task APageThatAllowsScriptsRunsASameOriginScriptAsync() {
        string script = UniquePath("script") + ".js";
        string page = UniquePath("scripted");
        await SavePageAsync(script, """
            <% ctx.SetContentType("text/javascript; charset=utf-8"); %>
            window.__pageScriptRan = true;
            export {};
            """);
        await SavePageAsync(page, "<% ctx.AllowScripts(); %>" + PageTemplate("Scripted page",
            $"<script type=\"module\" src=\"{script}\"></script>"));

        await Page.GotoAsync(page);

        Assert.That(await Page.EvaluateAsync<bool?>("window.__pageScriptRan"), Is.True);
    }
}
