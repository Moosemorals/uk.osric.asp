// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core;

public interface IContext {

    /// <summary>
    /// Logs a message to the environment
    /// </summary>
    /// <param name="message"></param>
    public void Log(string message);

    /// <summary>
    /// Write unescaped HTML to the output
    /// </summary>
    /// <param name="message"></param>
    public void WriteRaw(string message);

    /// <summary>
    /// Write escaped HTML to the output
    /// </summary>
    /// <param name="message"></param>
    public void Write(string message);

    public string Path { get; }
}
