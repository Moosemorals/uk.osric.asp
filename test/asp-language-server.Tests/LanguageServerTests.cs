// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace AspLanguageServer.Tests;

using System.Reflection;
using System.Text.Json.Nodes;
using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Runtime;

/// <summary>The protocol: what the editor sends, what comes back, and in what order.</summary>
[TestFixture]
public sealed class LanguageServerTests {

    private const string Uri = "file:///workspace/Pages/Seeds/page";

    [Test]
    public async Task InitializeAnswersTheCapabilitiesItHasAsync() {
        FakeTransport transport = new();
        transport.Send(Request(1, "initialize"));

        int exit = await RunAsync(transport, Checker(_ => []));

        JsonObject reply = Reply(transport, 1)!;
        Assert.Multiple(() => {
            Assert.That(exit, Is.Zero);
            Assert.That((string?)reply["result"]!["serverInfo"]!["name"]!, Is.EqualTo("asp-language-server"));
            Assert.That((bool)reply["result"]!["capabilities"]!["textDocumentSync"]!["openClose"]!, Is.True);
            Assert.That((int)reply["result"]!["capabilities"]!["textDocumentSync"]!["change"]!, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ADocumentsDiagnosticsArePositionedInTemplateSpaceAsync() {
        FakeTransport transport = new();
        transport.Send(Open(Uri, "<p>first</p>\n<% int broken = ; %>\n", version: 1));

        await RunAsync(transport, new CompilerPageChecker(new PageCompiler(), []));

        JsonObject diagnostic = Diagnostics(transport, Uri)[0]!.AsObject();
        Assert.Multiple(() => {
            Assert.That((int)diagnostic["range"]!["start"]!["line"]!, Is.EqualTo(1),
                "the second template line is the protocol's line 1");
            Assert.That((int)diagnostic["severity"]!, Is.EqualTo(1));
            Assert.That((string?)diagnostic["source"], Is.EqualTo("asp"));
            Assert.That((string?)diagnostic["code"], Is.EqualTo("CS1525").Or.EqualTo("CS1003").Or.EqualTo("CS1026"));
        });
    }

    [Test]
    public async Task ALiteralThatSwallowsTheCloserIsUnderlinedOnTheCloserAsync() {
        FakeTransport transport = new();
        // The template the spike used: the block ends at the first '%>', inside the string, so the
        // actionable character is the delimiter and not the quote the literal opened with.
        transport.Send(Open(Uri, "<% var x = \"%>\"; %>", version: 1));

        await RunAsync(transport, new CompilerPageChecker(new PageCompiler(), []));

        JsonObject warning = Diagnostics(transport, Uri)
            .Select(item => item!.AsObject())
            .Single(item => (string?)item["code"] == "TEMPLATE008");
        Assert.Multiple(() => {
            Assert.That((int)warning["range"]!["start"]!["line"]!, Is.Zero);
            Assert.That((int)warning["range"]!["start"]!["character"]!, Is.EqualTo(12),
                "0-based column 12 is the '%' of the pair that ended the block");
            Assert.That((int)warning["range"]!["end"]!["character"]!, Is.EqualTo(14), "the extent covers '%>'");
        });
    }

    [Test]
    public async Task ADiagnosticCoversTheTokensTheCompilerNamedAsync() {
        FakeTransport transport = new();
        transport.Send(Open(Uri, "<p>ok</p>", version: 1));

        await RunAsync(transport, Checker(_ => [
            new PageDiagnostic(
                PageDiagnosticSeverity.Warning,
                new TemplatePosition(3, 5),
                "TEMPLATE003: '>' has no matching '<%'.") {
                End = new TemplatePosition(3, 9),
            },
        ]));

        JsonObject diagnostic = Diagnostics(transport, Uri)[0]!.AsObject();
        Assert.Multiple(() => {
            Assert.That((int)diagnostic["range"]!["start"]!["line"]!, Is.EqualTo(2));
            Assert.That((int)diagnostic["range"]!["start"]!["character"]!, Is.EqualTo(4));
            Assert.That((int)diagnostic["range"]!["end"]!["character"]!, Is.EqualTo(8),
                "the end is exclusive, exactly as the template's is");
            Assert.That((int)diagnostic["severity"]!, Is.EqualTo(2));
            Assert.That((string?)diagnostic["code"], Is.EqualTo("TEMPLATE003"));
            Assert.That((string?)diagnostic["message"], Is.EqualTo("'>' has no matching '<%'."),
                "the code travels once, as a field rather than as a prefix");
        });
    }

    [Test]
    public async Task AChangeIsPublishedForItsOwnVersionAndNeverAnOlderOneAsync() {
        FakeTransport transport = new();
        transport.Send(Open(Uri, "clean", version: 1));
        transport.Send(Change(Uri, "broken", version: 2));
        transport.Send(Change(Uri, "clean again", version: 3));

        await RunAsync(transport, Checker(text => text == "broken" ? [Broken(1, 1)] : []));

        Assert.Multiple(() => {
            Assert.That(Versions(transport, Uri), Is.EqualTo(new int?[] { 1, 2, 3 }));
            Assert.That(Diagnostics(transport, Uri), Is.Empty, "the last answer is the one that stands");
        });
    }

    [Test]
    public async Task AChangedDocumentIsCheckedAgainAsync() {
        FakeTransport transport = new();
        transport.Send(Open(Uri, "clean", version: 1));
        transport.Send(Change(Uri, "broken", version: 2));

        await RunAsync(transport, Checker(text => text == "broken" ? [Broken(1, 1)] : []));

        Assert.That(Diagnostics(transport, Uri), Has.Count.EqualTo(1));
    }

    [Test]
    public async Task AClosedDocumentHasItsDiagnosticsClearedAsync() {
        FakeTransport transport = new();
        transport.Send(Open(Uri, "broken", version: 1));
        transport.Send(Close(Uri));

        await RunAsync(transport, Checker(_ => [Broken(1, 1)]));

        Assert.That(Diagnostics(transport, Uri), Is.Empty);
    }

    [Test]
    public async Task AnUnknownRequestIsRefusedAsync() {
        FakeTransport transport = new();
        transport.Send(Request(9, "textDocument/hover"));

        await RunAsync(transport, Checker(_ => []));

        JsonObject reply = Reply(transport, 9)!;
        Assert.Multiple(() => {
            Assert.That((int)reply["error"]!["code"]!, Is.EqualTo(-32601));
            Assert.That((string?)reply["error"]!["message"], Does.Contain("hover"));
        });
    }

    [Test]
    public async Task ShutdownIsAnsweredAndExitEndsTheLoopAsync() {
        FakeTransport transport = new();
        transport.Send(Request(2, "shutdown"));
        transport.Send(Notification("exit"));

        int exit = await RunAsync(transport, Checker(_ => []));

        Assert.Multiple(() => {
            Assert.That(exit, Is.Zero);
            Assert.That(Reply(transport, 2)!["result"], Is.Null);
        });
    }

    [Test]
    public async Task ADocumentInAnotherLanguageIsLeftAloneAsync() {
        FakeTransport transport = new();
        JsonObject opened = Open(Uri, "body { color: red; }", version: 1);
        opened["params"]!["textDocument"]!["languageId"] = "css";
        transport.Send(opened);

        await RunAsync(transport, Checker(_ => [Broken(1, 1)]));

        Assert.That(transport.Written, Is.Empty);
    }

    [Test]
    public async Task AServerWithNoSeedsStillChecksItsOwnDocumentAsync() {
        FakeTransport transport = new();
        transport.Send(Open(Uri, "<h1>Just text</h1>", version: 1));

        await RunAsync(transport, new CompilerPageChecker(new PageCompiler(), []));

        Assert.That(Diagnostics(transport, Uri), Is.Empty);
    }

    [Test]
    public async Task ALibrarysErrorIsPublishedAgainstItsFileAsync() {
        string folder = Path.Combine(Path.GetTempPath(), "asp-language-server-" + Guid.NewGuid().ToString("N"));
        string library = Path.Combine(folder, "_lib", "broken");
        string page = Path.Combine(folder, "page");
        Directory.CreateDirectory(Path.Combine(folder, "_lib"));
        await File.WriteAllTextAsync(
            library, "<%! namespace Site { public static class Broken { public static int Value = \"no\"; } } %>");
        await File.WriteAllTextAsync(page, "<%! using Site; %><%= Broken.Value.ToString() %><p>");

        try {
            PageCompiler compiler = new();
            SeedSources seeds = SeedSources.Load(folder, compiler);
            FakeTransport transport = new();
            transport.Send(Open(new Uri(page).AbsoluteUri, await File.ReadAllTextAsync(page), version: 1));

            await RunAsync(transport, new CompilerPageChecker(compiler, seeds.Libraries), seeds);

            string libraryUri = new Uri(library).AbsoluteUri;
            Assert.Multiple(() => {
                Assert.That(Diagnostics(transport, libraryUri), Has.Count.EqualTo(1),
                    "the library's error belongs on the library's file, not on the page that named it");
                Assert.That(Diagnostics(transport, new Uri(page).AbsoluteUri), Is.Empty,
                    "the page itself is clean");
            });
        } finally {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Test]
    public async Task ASeedLibraryIsFoundInTheRepositoryAsync() {
        PageCompiler compiler = new();
        SeedSources seeds = SeedSources.Load(SeedsFolder(), compiler);
        FakeTransport transport = new();
        transport.Send(Open(Uri, "<%! using Site.Document; %><%= Document.Missing() %>", version: 1));

        await RunAsync(transport, new CompilerPageChecker(compiler, seeds.Libraries), seeds);

        JsonObject diagnostic = Diagnostics(transport, Uri)[0]!.AsObject();
        Assert.Multiple(() => {
            Assert.That((string?)diagnostic["code"], Does.StartWith("CS"));
            Assert.That((string?)diagnostic["message"],
                Does.Contain("does not contain a definition for 'Missing'"),
                "the library came from the seed file, so the type is known and only the member is not");
        });
    }

    [Test]
    public async Task AnOpenDocumentIsPublishedWithItsVersionAsync() {
        FakeTransport transport = new();
        transport.Send(Open(Uri, "clean", version: 7));

        await RunAsync(transport, Checker(_ => []));

        JsonObject published = Published(transport, Uri)[0];
        Assert.That((int)published["params"]!["version"]!, Is.EqualTo(7));
    }

    private static Task<int> RunAsync(FakeTransport transport, IPageChecker checker, SeedSources? seeds = null) =>
        new LanguageServer(transport, checker, seeds ?? SeedSources.Empty).RunAsync(CancellationToken.None);

    private static IPageChecker Checker(Func<string, IReadOnlyList<PageDiagnostic>> check) => new FixedChecker(check);

    private static PageDiagnostic Broken(int line, int column) =>
        new(PageDiagnosticSeverity.Error, new TemplatePosition(line, column), "CS1002: ; expected");

    private static JsonObject Open(string uri, string text, int version) => new() {
        ["jsonrpc"] = "2.0",
        ["method"] = "textDocument/didOpen",
        ["params"] = new JsonObject {
            ["textDocument"] = new JsonObject {
                ["uri"] = uri,
                ["languageId"] = "asp",
                ["version"] = version,
                ["text"] = text,
            },
        },
    };

    private static JsonObject Change(string uri, string text, int version) => new() {
        ["jsonrpc"] = "2.0",
        ["method"] = "textDocument/didChange",
        ["params"] = new JsonObject {
            ["textDocument"] = new JsonObject {
                ["uri"] = uri,
                ["version"] = version,
            },
            ["contentChanges"] = new JsonArray(
                new JsonObject { ["text"] = text }),
        },
    };

    private static JsonObject Close(string uri) => new() {
        ["jsonrpc"] = "2.0",
        ["method"] = "textDocument/didClose",
        ["params"] = new JsonObject {
            ["textDocument"] = new JsonObject { ["uri"] = uri },
        },
    };

    private static JsonObject Request(int id, string method) => new() {
        ["jsonrpc"] = "2.0",
        ["id"] = id,
        ["method"] = method,
    };

    private static JsonObject Notification(string method) => new() {
        ["jsonrpc"] = "2.0",
        ["method"] = method,
    };

    private static JsonObject? Reply(FakeTransport transport, int id) => transport.Written
        .LastOrDefault(message => (int?)message["id"] == id);

    private static List<JsonObject> Published(FakeTransport transport, string uri) => [.. transport.Written
        .Where(message => (string?)message["method"] == "textDocument/publishDiagnostics"
            && (string?)message["params"]!["uri"] == uri)];

    private static JsonArray Diagnostics(FakeTransport transport, string uri) =>
        Published(transport, uri) is { Count: > 0 } messages
            ? messages[^1]["params"]!["diagnostics"]!.AsArray()
            : [];

    private static int?[] Versions(FakeTransport transport, string uri) => [.. Published(transport, uri)
        .Select(message => (int?)message["params"]!["version"])];

    private static string SeedsFolder() => Path.Combine(
        typeof(LanguageServerTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "RepoRoot")
            .Value!,
        "src", "uk.osric.asp.Web", "Pages", "Seeds");

    private sealed class FixedChecker(Func<string, IReadOnlyList<PageDiagnostic>> check) : IPageChecker {
        public IReadOnlyList<PageDiagnostic> Check(string template) => check(template);
    }
}
