// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant.Shell;

using uk.osric.asp.Core.Shell;

/// <summary>The shell's applets, by the word that runs them.</summary>
/// <remarks>
/// The table BusyBox keeps: one row per command, and everything about a command is on its row's
/// applet. <c>help</c> reads this rather than a list of its own, so the help text cannot fall behind
/// the commands.
/// </remarks>
internal sealed class ShellRegistry {

    private readonly Dictionary<string, ShellApplet> _byName;

    /// <summary>Builds the table from the applets, in ordinal order.</summary>
    /// <param name="applets">Every applet the shell answers.</param>
    public ShellRegistry(IEnumerable<ShellApplet> applets) {
        ArgumentNullException.ThrowIfNull(applets);

        All = [.. applets.OrderBy(applet => applet.Name, StringComparer.Ordinal)];
        _byName = All.ToDictionary(applet => applet.Name, StringComparer.Ordinal);
    }

    /// <summary>Every applet, in ordinal order.</summary>
    public IReadOnlyList<ShellApplet> All { get; }

    /// <summary>The applet a word names, or null when the shell does not answer it.</summary>
    /// <param name="name">The command word.</param>
    public ShellApplet? Find(string name) => _byName.GetValueOrDefault(name);
}

/// <summary>What an applet is given for one run.</summary>
/// <param name="Line">The parsed command line.</param>
/// <param name="Paths">The paths its operands named, expanded and validated.</param>
/// <param name="Store">The one read seam over the page store.</param>
/// <param name="Output">Where the answer is written.</param>
/// <param name="Registry">The applets, which <c>help</c> reads.</param>
internal sealed class ShellContext(
    ShellCommandLine line,
    IReadOnlyList<PageNode> paths,
    IPageStoreReader store,
    IShellOutput output,
    ShellRegistry registry) {

    /// <summary>The parsed command line.</summary>
    public ShellCommandLine Line { get; } = line;

    /// <summary>The paths the operands named, in the order they were written.</summary>
    public IReadOnlyList<PageNode> Paths { get; } = paths;

    /// <summary>The one read seam over the page store.</summary>
    public IPageStoreReader Store { get; } = store;

    /// <summary>Where the answer is written.</summary>
    public IShellOutput Output { get; } = output;

    /// <summary>The applets, which <c>help</c> reads.</summary>
    public ShellRegistry Registry { get; } = registry;
}
