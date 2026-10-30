// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web;

using uk.osric.asp.Core;

public class SimpleContext(ILogger logger, TextWriter writer, string path) : IContext {

    public string Path => path;

    public void Log(string message) {
        logger.LogInformation("Path: {Path} - {Message}", path, message);
    }

    public void Write(string message) {
        writer.Write(System.Net.WebUtility.HtmlEncode(message));
    }

    public void WriteRaw(string message) {
        writer.Write(message);
    }
}
