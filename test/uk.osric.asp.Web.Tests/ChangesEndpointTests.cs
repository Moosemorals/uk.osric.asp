// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.AspNetCore.Http;
using uk.osric.asp.Web.Admin;

[TestFixture]
public sealed class ChangesEndpointTests {

    private readonly List<string> _files = [];

    [TearDown]
    public void RemoveFiles() {
        foreach (string file in _files) {
            File.Delete(file);
        }

        _files.Clear();
    }

    [Test]
    public async Task EachCommitLinksToItsPageOnGitHubAsync() {
        const string first = "0123456789abcdef0123456789abcdef01234567";
        const string second = "fedcba9876543210fedcba9876543210fedcba98";

        string body = await RenderAsync($"{first} First commit\n{second} Second commit\n");

        Assert.Multiple(() => {
            Assert.That(body, Does.Contain($"href=\"{ChangesEndpoint.RepositoryUrl}/commit/{first}\""));
            Assert.That(body, Does.Contain($"href=\"{ChangesEndpoint.RepositoryUrl}/commit/{second}\""));
            Assert.That(body, Does.Contain("<code>0123456</code> First commit"), "the link text is the short hash");
            Assert.That(body.IndexOf("First commit", StringComparison.Ordinal),
                Is.LessThan(body.IndexOf("Second commit", StringComparison.Ordinal)), "newest first, as git prints it");
            Assert.That(body, Does.Contain("2 commits, newest first"));
        });
    }

    [Test]
    public async Task TheSubjectIsEncodedAsync() {
        string body = await RenderAsync("0123456789abcdef0123456789abcdef01234567 Escape <script> & co\n");

        Assert.Multiple(() => {
            Assert.That(body, Does.Contain("&lt;script&gt; &amp; co"));
            Assert.That(body, Does.Not.Contain("<script>"));
        });
    }

    [Test]
    public async Task ALineThatIsNotACommitIsShownUnlinkedAsync() {
        string body = await RenderAsync("not a commit line\n");

        Assert.Multiple(() => {
            Assert.That(body, Does.Contain("<li>not a commit line</li>"));
            Assert.That(body, Does.Not.Contain("/commit/"));
        });
    }

    [Test]
    public async Task ABuildWithoutAChangelistSaysSoAsync() {
        DefaultHttpContext context = TestHttp.CreateContext(null, "GET", "/admin/changes");

        await ChangesEndpoint.HandleAsync(context, Options(MissingPath()));

        string body = await TestHttp.ReadBodyAsync(context);
        Assert.That(body, Does.Contain("This build carries no changelist"));
    }

    [Test]
    public void ARelativeSettingIsResolvedBesideTheAssembly() {
        string path = ChangesEndpoint.PathOf(Options("changes.txt"));

        Assert.That(path, Is.EqualTo(Path.Combine(AppContext.BaseDirectory, "changes.txt")));
    }

    private async Task<string> RenderAsync(string contents) {
        string file = Path.Combine(Path.GetTempPath(), $"uk.osric.asp.changes.{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(file, contents);
        _files.Add(file);

        DefaultHttpContext context = TestHttp.CreateContext(null, "GET", "/admin/changes");
        await ChangesEndpoint.HandleAsync(context, Options(file));

        return await TestHttp.ReadBodyAsync(context);
    }

    private static string MissingPath() =>
        Path.Combine(Path.GetTempPath(), $"uk.osric.asp.missing.{Guid.NewGuid():N}.txt");

    private static AdminOptions Options(string path) => new() { Changelist = path };
}
