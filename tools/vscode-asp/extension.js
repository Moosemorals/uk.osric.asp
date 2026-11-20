// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

// The live half of the template language: a Language Server Protocol client for the .NET server in
// tools/asp-language-server. It spawns the server, forwards every `asp` document it opens or changes,
// and shows what comes back as squiggles.
//
// The extension host loads an extension's `main` as CommonJS, so `require` is the only way to reach
// the editor's API: this is the one script in the repository that cannot be an ES module. There is no
// build step and no dependency, and `vscode-languageclient` is not needed for four messages.

'use strict';

const vscode = require('vscode');
const { spawn } = require('node:child_process');

const language = 'asp';
const publishDiagnostics = 'textDocument/publishDiagnostics';

let session;

function activate(context) {
    const diagnostics = vscode.languages.createDiagnosticCollection(language);
    context.subscriptions.push(diagnostics);

    const folder = vscode.workspace.workspaceFolders?.[0];
    if (!folder) {
        // A single file with no workspace has nowhere to resolve the seeds or the built server from.
        return;
    }

    session = startSession(diagnostics, folder);

    context.subscriptions.push(
        vscode.workspace.onDidOpenTextDocument(document => session.open(document)),
        vscode.workspace.onDidChangeTextDocument(event => session.change(event.document)),
        vscode.workspace.onDidCloseTextDocument(document => session.close(document)),
    );

    for (const document of vscode.workspace.textDocuments) {
        session.open(document);
    }
}

function deactivate() {
    session?.stop();
    session = undefined;
}

function startSession(diagnostics, folder) {
    const configuration = vscode.workspace.getConfiguration('aspLanguageServer', folder.uri);
    const command = configuration.get('command') || 'dotnet';
    const serverPath = resolved(configuration.get('path'), folder);
    const seeds = resolved(configuration.get('seeds'), folder);

    const child = spawn(command, [serverPath, '--seeds', seeds], { stdio: ['pipe', 'pipe', 'pipe'] });

    let buffered = Buffer.alloc(0);
    let nextId = 1;

    child.stdout.on('data', chunk => {
        buffered = Buffer.concat([buffered, chunk]);
        buffered = drain(buffered, message => receive(message));
    });

    child.stderr.on('data', chunk => {
        console.error(`asp-language-server: ${chunk.toString()}`);
    });

    child.on('exit', code => {
        if (code !== 0 && code !== null) {
            vscode.window.showWarningMessage(`The ASP template language server stopped (exit ${code}).`);
        }
    });

    function send(message) {
        const body = Buffer.from(JSON.stringify(message), 'utf8');
        child.stdin.write(`Content-Length: ${body.length}\r\n\r\n`);
        child.stdin.write(body);
    }

    function request(method, params) {
        send({ jsonrpc: '2.0', id: nextId++, method, params });
    }

    function notify(method, params) {
        send({ jsonrpc: '2.0', method, params });
    }

    function receive(message) {
        if (message.method === publishDiagnostics) {
            const { uri, diagnostics: found } = message.params;
            diagnostics.set(vscode.Uri.parse(uri), found.map(toDiagnostic));
            return;
        }

        if (message.error) {
            console.error(`asp-language-server: ${message.error.message}`);
        }
    }

    request('initialize', {
        processId: process.pid,
        rootUri: folder.uri.toString(),
        capabilities: {},
    });
    notify('initialized', {});

    return {
        open(document) {
            if (document.languageId !== language) {
                return;
            }

            notify('textDocument/didOpen', {
                textDocument: {
                    uri: document.uri.toString(),
                    languageId: language,
                    version: document.version,
                    text: document.getText(),
                },
            });
        },
        change(document) {
            if (document.languageId !== language) {
                return;
            }

            notify('textDocument/didChange', {
                textDocument: { uri: document.uri.toString(), version: document.version },
                contentChanges: [{ text: document.getText() }],
            });
        },
        close(document) {
            if (document.languageId !== language) {
                return;
            }

            notify('textDocument/didClose', { textDocument: { uri: document.uri.toString() } });
        },
        stop() {
            // The protocol's own order: ask to shut down, say goodbye, then close the pipe.
            request('shutdown', {});
            notify('exit', {});
            child.stdin.end();
        },
    };
}

// Reads every complete message out of the buffer: a Content-Length header block, then that many bytes
// of UTF-8 JSON. A partial message stays buffered until the rest of it arrives.
function drain(buffer, handle) {
    let rest = buffer;

    while (true) {
        const headerEnd = rest.indexOf('\r\n\r\n');
        if (headerEnd < 0) {
            return rest;
        }

        const headers = rest.subarray(0, headerEnd).toString('ascii');
        const match = /Content-Length:\s*(\d+)/i.exec(headers);
        if (!match) {
            // Nothing can be framed from here on; drop the unreadable block rather than wedge.
            rest = rest.subarray(headerEnd + 4);
            continue;
        }

        const length = Number(match[1]);
        const start = headerEnd + 4;
        if (rest.length < start + length) {
            return rest;
        }

        handle(JSON.parse(rest.subarray(start, start + length).toString('utf8')));
        rest = rest.subarray(start + length);
    }
}

function toDiagnostic(item) {
    const range = new vscode.Range(
        item.range.start.line,
        item.range.start.character,
        item.range.end.line,
        item.range.end.character,
    );
    const severity = item.severity === 1
        ? vscode.DiagnosticSeverity.Error
        : vscode.DiagnosticSeverity.Warning;
    const diagnostic = new vscode.Diagnostic(range, item.message, severity);
    diagnostic.source = item.source || language;

    if (item.code) {
        diagnostic.code = item.code;
    }

    return diagnostic;
}

// A setting names a path the way a task does: `${workspaceFolder}` is the folder the workspace opened.
function resolved(path, folder) {
    return String(path || '').replace('${workspaceFolder}', folder.uri.fsPath);
}

module.exports = { activate, deactivate };
