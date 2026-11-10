// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant.Shell;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Core.Shell;
using uk.osric.asp.Core.Text;

/// <summary>Turns a command's operands into the nodes they name.</summary>
/// <remarks>
/// One stage, for every command: the applet declares what kind of operand it takes and this decides
/// what the words mean. A path is validated and looked up, a prefix may be a folder, a text operand
/// is left alone, and a word that names nothing is a refusal — never an empty answer that looks like a
/// command which found nothing.
/// </remarks>
/// <param name="paths">The policy, which says what a key may be.</param>
internal sealed class ShellExpansion(PagePathPolicy paths) {

    /// <summary>The nodes the operands name, or the refusal that says why one does not name any.</summary>
    /// <param name="arguments">The operands, with the kind the applet declared for each.</param>
    /// <param name="store">The store to resolve them against.</param>
    /// <param name="cancellationToken">The run's token.</param>
    public async Task<(IReadOnlyList<ShellPath> Paths, ShellResult? Refusal)> ExpandAsync(
        IReadOnlyList<ShellArgument> arguments,
        IPageStoreView store,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(store);

        List<ShellPath> named = [];
        foreach (ShellArgument argument in arguments) {
            if (argument.Kind == ShellOperandKind.Text) {
                continue;
            }

            string text = argument.Word.Text;
            PathValidationResult checkedPrefix = argument.Kind == ShellOperandKind.Prefix
                ? PagePathPolicy.ValidatePrefix(text)
                : paths.Validate(text, write: false);
            if (!checkedPrefix.IsSuccess) {
                return ([], ShellResult.Refused(checkedPrefix.Error!, "Shell path refused"));
            }

            // A prefix with no text is the root, which is the whole store and always there.
            PagePath key = checkedPrefix.Path ?? PagePath.Normalize("/");
            if (paths.ShadowedBy(key) is { } owner && !string.Equals(owner.Key, key.Key, StringComparison.Ordinal)) {
                return ([], ShellResult.Refused(
                    $"The key {MessageText.Sanitize(key.Key)} is beneath the shell file {MessageText.Sanitize(owner.Key)}. Read {MessageText.Sanitize(owner.Key)} instead.",
                    $"Shell path refused: {MessageText.Sanitize(key.Key)}"));
            }

            IReadOnlyList<ShellPath> nodes = await store.NodesAsync(cancellationToken);
            ShellPath? node = nodes.FirstOrDefault(item => string.Equals(item.Key.Key, key.Key, StringComparison.Ordinal));
            if (node is null) {
                return ([], ShellResult.Refused(
                    $"No key {MessageText.Sanitize(key.Key)} is stored. ls {Folder(key)} lists what is there.",
                    $"Shell path refused: {MessageText.Sanitize(key.Key)}"));
            }
            named.Add(node);
        }

        return (named, null);
    }

    private static string Folder(PagePath key) {
        string? folder = ShellTree.FolderOf(key.Key);
        return MessageText.Sanitize(folder ?? "/");
    }
}
