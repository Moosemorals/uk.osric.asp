// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace AspLanguageServer;

using System.Text.Json.Nodes;
using uk.osric.asp.Core.Compilation;

/// <summary>The Language Server Protocol: open a document, check it when it changes, publish what the compiler said.</summary>
/// <remarks>
/// <para>
/// The server is one loop, so messages are answered in the order they arrive and a version can never be
/// published after a newer one. It offers diagnostics and nothing else: no completion, no hover and no
/// formatting, because the compiler answers those for a generated source, not for a template.
/// </para>
/// <para>
/// A check publishes for the document and for any seed library it reached, because a library's own
/// error belongs on the library's file rather than on the page that named it.
/// </para>
/// </remarks>
internal sealed class LanguageServer(
    ILspTransport transport,
    IPageChecker checker,
    SeedSources seeds) {

    private const string Language = "asp";

    private readonly OpenDocumentSet _documents = new();

    /// <summary>Answers messages until the editor closes the stream or says goodbye.</summary>
    /// <param name="cancellationToken">Stops the loop.</param>
    /// <returns>Zero, which is a protocol-conforming exit.</returns>
    public async Task<int> RunAsync(CancellationToken cancellationToken) {
        while (true) {
            JsonObject? message = await transport.ReadAsync(cancellationToken);
            if (message is null) {
                return 0;
            }

            if (message["method"] is not JsonValue methodValue || !methodValue.TryGetValue(out string? method)) {
                // A response to a request this server made, and it makes none.
                continue;
            }

            JsonNode? id = message["id"];
            switch (method) {
                case "initialize":
                    await ReplyAsync(id, Initialize(), cancellationToken);
                    break;
                case "initialized":
                case "$/cancelRequest":
                    break;
                case "textDocument/didOpen":
                    await DidOpenAsync(message["params"] as JsonObject, cancellationToken);
                    break;
                case "textDocument/didChange":
                    await DidChangeAsync(message["params"] as JsonObject, cancellationToken);
                    break;
                case "textDocument/didClose":
                    await DidCloseAsync(message["params"] as JsonObject, cancellationToken);
                    break;
                case "shutdown":
                    await ReplyAsync(id, null, cancellationToken);
                    break;
                case "exit":
                    return 0;
                default:
                    if (id is not null) {
                        await ReplyErrorAsync(id, -32601, $"Method not found: {method}", cancellationToken);
                    }
                    break;
            }
        }
    }

    private async Task DidOpenAsync(JsonObject? parameters, CancellationToken cancellationToken) {
        if (parameters?["textDocument"] is not JsonObject document) {
            return;
        }

        string? uri = Text(document["uri"]);
        string? text = Text(document["text"]);
        if (uri is null || text is null || !IsTemplate(document["languageId"])) {
            return;
        }

        await CheckAndPublishAsync(_documents.Opened(uri, text, Number(document["version"])), cancellationToken);
    }

    private async Task DidChangeAsync(JsonObject? parameters, CancellationToken cancellationToken) {
        if (parameters?["textDocument"] is not JsonObject document) {
            return;
        }

        string? uri = Text(document["uri"]);
        if (uri is null || parameters["contentChanges"] is not JsonArray changes) {
            return;
        }

        // Full sync: the last change carries the whole document, whatever earlier ones said.
        string? text = null;
        foreach (JsonNode? change in changes) {
            text = Text(change?["text"]) ?? text;
        }

        if (text is null || _documents.Changed(uri, text, Number(document["version"])) is not { } changed) {
            return;
        }

        await CheckAndPublishAsync(changed, cancellationToken);
    }

    private async Task DidCloseAsync(JsonObject? parameters, CancellationToken cancellationToken) {
        string? uri = Text((parameters?["textDocument"] as JsonObject)?["uri"]);
        if (uri is null || !_documents.Closed(uri)) {
            return;
        }

        // The document is gone, so its squiggles go with it.
        await PublishAsync(uri, version: null, [], cancellationToken);
    }

    private async Task CheckAndPublishAsync(OpenDocument document, CancellationToken cancellationToken) {
        IReadOnlyList<PageDiagnostic> diagnostics;
        try {
            diagnostics = checker.Check(document.Text);
        } catch (InvalidOperationException) {
            // A compiler fault is this tool's bug, not the author's: answer nothing rather than take
            // the editor's server down, and let the next edit replace whatever was there.
            return;
        }

        // A newer change may have arrived while this check ran, and its own publish is the one that
        // counts; this answer is already out of date.
        if (_documents.Find(document.Uri) is not { } current || current.Version != document.Version) {
            return;
        }

        Dictionary<string, JsonArray> byFile = [];
        foreach (PageDiagnostic diagnostic in diagnostics) {
            string uri = diagnostic.Source is { } key
                ? seeds.UriOf(key) ?? document.Uri
                : document.Uri;

            if (!byFile.TryGetValue(uri, out JsonArray? items)) {
                byFile[uri] = items = [];
            }

            items.Add(LspDiagnostics.ToJson(diagnostic));
        }

        // The checked document always answers, so a fixed template clears rather than keeping its
        // old diagnostics, and a clean one says so with nothing.
        byFile.TryAdd(document.Uri, []);

        foreach ((string uri, JsonArray items) in byFile) {
            // A version belongs to the document it names; a library's answer has no version of its own.
            await PublishAsync(uri, uri == document.Uri ? document.Version : null, items, cancellationToken);
        }
    }

    private Task PublishAsync(string uri, int? version, JsonArray diagnostics, CancellationToken cancellationToken) {
        JsonObject parameters = new() {
            ["uri"] = uri,
            ["diagnostics"] = diagnostics,
        };
        if (version is { } value) {
            parameters["version"] = value;
        }

        return transport.WriteAsync(new JsonObject {
            ["jsonrpc"] = "2.0",
            ["method"] = "textDocument/publishDiagnostics",
            ["params"] = parameters,
        }, cancellationToken);
    }

    private Task ReplyAsync(JsonNode? id, JsonNode? result, CancellationToken cancellationToken) =>
        transport.WriteAsync(new JsonObject {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["result"] = result,
        }, cancellationToken);

    private Task ReplyErrorAsync(JsonNode? id, int code, string message, CancellationToken cancellationToken) =>
        transport.WriteAsync(new JsonObject {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["error"] = new JsonObject {
                ["code"] = code,
                ["message"] = message,
            },
        }, cancellationToken);

    private static JsonObject Initialize() => new() {
        ["capabilities"] = new JsonObject {
            ["textDocumentSync"] = new JsonObject {
                ["openClose"] = true,
                // 1 is full: the editor sends the whole document, so the server keeps no edit log.
                ["change"] = 1,
            },
        },
        ["serverInfo"] = new JsonObject {
            ["name"] = "asp-language-server",
            ["version"] = "0.1.0",
        },
    };

    private static bool IsTemplate(JsonNode? languageId) =>
        languageId is not JsonValue value
        || !value.TryGetValue(out string? id)
        || string.Equals(id, Language, StringComparison.Ordinal);

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue(out string? text) ? text : null;

    private static int? Number(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue(out int number) ? number : null;
}
