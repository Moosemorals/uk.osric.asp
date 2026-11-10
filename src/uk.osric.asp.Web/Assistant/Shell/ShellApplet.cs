// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant.Shell;

using uk.osric.asp.Core.Shell;

/// <summary>One command of the virtual shell: its usage, its syntax and its behaviour, in one place.</summary>
/// <remarks>
/// An applet is the whole of a command, the way a BusyBox applet is: its name, the usage line help
/// prints, the syntax the kernel validates against, the notes help adds when it is asked about it, and
/// the code that runs it. Nothing outside an applet knows what its options mean, and adding a command
/// is adding a file and a row in the registry.
/// </remarks>
internal abstract class ShellApplet {

    /// <summary>The command's word.</summary>
    public abstract string Name { get; }

    /// <summary>The whole form it is called in, options and operands included.</summary>
    public abstract string Usage { get; }

    /// <summary>One line saying what it does, which help prints after the usage.</summary>
    public abstract string Description { get; }

    /// <summary>The syntax that one line cannot carry, when there is some.</summary>
    public virtual string? Notes => null;

    /// <summary>What the kernel accepts on its command line.</summary>
    public abstract ShellSyntax Syntax { get; }

    /// <summary>Runs the command.</summary>
    /// <param name="line">The parsed command line.</param>
    /// <param name="context">The shared tools: the expanded paths, the store, the output and the registry.</param>
    /// <param name="cancellationToken">The run's token.</param>
    public abstract ValueTask<ShellResult> RunAsync(
        ShellCommandLine line,
        ShellContext context,
        CancellationToken cancellationToken);
}

/// <summary>How a command, or a stage of the shell, ended.</summary>
/// <param name="IsSuccess">Whether it did what it was asked.</param>
/// <param name="Summary">One line for the tool result's summary.</param>
/// <param name="Error">The refusal, when it failed.</param>
internal sealed record ShellResult(bool IsSuccess, string Summary, string? Error = null) {

    /// <summary>What a command that worked answers with.</summary>
    /// <param name="summary">One line for the tool result's summary.</param>
    public static ShellResult Ok(string summary) => new(true, summary);

    /// <summary>What a command that refused answers with, and the reason it gives.</summary>
    /// <param name="error">The refusal.</param>
    /// <param name="summary">One line for the tool result's summary.</param>
    public static ShellResult Refused(string error, string summary) => new(false, summary, error);
}

/// <summary>What the kernel has to say about a line, in the shape the tool result wants.</summary>
/// <param name="IsSuccess">Whether the command ran.</param>
/// <param name="Output">What it wrote, when it ran.</param>
/// <param name="Truncated">Whether a limit or the result budget left something out.</param>
/// <param name="Summary">One line for the tool result's summary.</param>
/// <param name="Error">The refusal, when it failed.</param>
internal sealed record ShellOutcome(bool IsSuccess, string Output, bool Truncated, string Summary, string? Error) {

    /// <summary>An answer.</summary>
    /// <param name="output">What the command wrote.</param>
    /// <param name="truncated">Whether something was left out.</param>
    /// <param name="summary">One line for the tool result's summary.</param>
    public static ShellOutcome Answered(string output, bool truncated, string summary) =>
        new(true, output, truncated, summary, null);

    /// <summary>A refusal.</summary>
    /// <param name="error">The refusal.</param>
    /// <param name="summary">One line for the tool result's summary.</param>
    public static ShellOutcome Refused(string error, string summary) =>
        new(false, string.Empty, false, summary, error);
}
