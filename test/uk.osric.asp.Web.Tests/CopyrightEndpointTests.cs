// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.AspNetCore.Http;
using uk.osric.asp.Runtime;
using uk.osric.asp.Web.Admin;

[TestFixture]
public sealed class CopyrightEndpointTests {

    private string _folder = string.Empty;

    [SetUp]
    public void CreateFolder() {
        _folder = Path.Combine(Path.GetTempPath(), "copyright-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(Path.Combine(_folder, "LICENSES"));
    }

    [TearDown]
    public void DeleteFolder() {
        Directory.Delete(_folder, recursive: true);
    }

    [Test]
    public async Task ThePageReadsEveryFileTheBuildCarries() {
        Write("LICENSE", "MIT License\n\nCopyright 2026 Osric Wilkinson");
        Write("THIRD-PARTY-NOTICES.md", "## Third-party notices\n\n### Acornima 1.9.1\n\nA parser.");
        Write("REUSE.toml", "version = 1\n");
        Write(Path.Combine("LICENSES", "MIT.txt"), "Permission is hereby granted");
        Write(Path.Combine("LICENSES", "ISC.txt"), "Permission to use, copy, modify");

        string body = await RenderAsync();

        Assert.Multiple(() => {
            Assert.That(body, Does.Contain("Copyright 2026 Osric Wilkinson"));
            Assert.That(body, Does.Contain("<h2"), "the notices are rendered as Markdown");
            Assert.That(body, Does.Contain("Acornima 1.9.1"));
            Assert.That(body, Does.Contain("id=\"licence-mit\""));
            Assert.That(body, Does.Contain("id=\"licence-isc\""));
            Assert.That(body.IndexOf("id=\"licence-isc\"", StringComparison.Ordinal),
                Is.LessThan(body.IndexOf("id=\"licence-mit\"", StringComparison.Ordinal)),
                "the texts are in name order");
            Assert.That(body, Does.Contain("version = 1"), "the REUSE file is shown as it is");
            Assert.That(body, Does.Contain("Copyright and licences"), "the page names itself");
        });
    }

    // A build that did not come through the Makefile carries none of them, and the page has to say
    // what is missing rather than fail.
    [Test]
    public async Task AMissingFileIsNamedAndTheRestStillRenders() {
        Write("LICENSE", "MIT License");

        string body = await RenderAsync();

        Assert.Multiple(() => {
            Assert.That(body, Does.Contain("MIT License"));
            Assert.That(body, Does.Contain("does not carry <code>THIRD-PARTY-NOTICES.md</code>"));
            Assert.That(body, Does.Contain("does not carry its <code>LICENSES</code> folder"));
            Assert.That(body, Does.Contain("does not carry <code>REUSE.toml</code>"));
        });
    }

    [Test]
    public async Task ThePageIsRenderedForAVisitorWithNoAccount() {
        DefaultHttpContext context = TestHttp.CreateContext(null, "GET", "/admin/copyright");

        await CopyrightEndpoint.HandleAsync(
            context, new LicenceLibrary(_folder), new MarkdownRenderer(), CancellationToken.None);

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
    }

    private async Task<string> RenderAsync() {
        DefaultHttpContext context = TestHttp.CreateContext(null, "GET", "/admin/copyright");

        await CopyrightEndpoint.HandleAsync(
            context, new LicenceLibrary(_folder), new MarkdownRenderer(), CancellationToken.None);

        return await TestHttp.ReadBodyAsync(context);
    }

    private void Write(string name, string text) {
        string path = Path.Combine(_folder, name);
        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }
}
