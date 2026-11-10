// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Shell;

using System.Globalization;

/// <summary>What a command reads an operand as.</summary>
public enum ShellOperandKind {

    /// <summary>A word the command reads as text: <c>grep</c>'s query.</summary>
    Text,

    /// <summary>A path that must name a page: <c>cat</c>'s file.</summary>
    Path,

    /// <summary>A path that may name a folder, or a pattern: <c>ls</c>'s directory.</summary>
    Prefix,
}

/// <summary>One operand a command takes, and how many of it.</summary>
/// <param name="Name">What the operand is called in a usage line.</param>
/// <param name="Kind">How the command reads it.</param>
/// <param name="Minimum">How many are required.</param>
/// <param name="Maximum">How many are accepted.</param>
public sealed record ShellOperand(string Name, ShellOperandKind Kind, int Minimum = 1, int Maximum = 1);

/// <summary>An option that takes a value, written <c>-n 5</c>, <c>-n5</c> or <c>-name pattern</c>.</summary>
/// <param name="Letter">The letter after the dash.</param>
/// <param name="Name">What the value is, in the wording a refusal uses: "a number", "a pattern".</param>
/// <param name="Numeric">Whether the value has to be a number.</param>
public sealed record ShellValueOption(char Letter, string Name, bool Numeric = true);

/// <summary>One operand as the line gave it, with the kind the command declared for it.</summary>
/// <param name="Kind">How the command reads it.</param>
/// <param name="Word">The word itself, quoting and all.</param>
public sealed record ShellArgument(ShellOperandKind Kind, ShellWord Word);

/// <summary>A command line that matched its command's syntax, or the refusal that says it did not.</summary>
/// <param name="Line">What was matched, or null when the line was refused.</param>
/// <param name="Error">A safe message, or null when the line matched.</param>
public sealed record ShellMatchResult(ShellCommandLine? Line, string? Error);

/// <summary>A command's command line: the flags and values it was given, and its operands.</summary>
/// <param name="Flags">The flags, as the letters that were written.</param>
/// <param name="Values">The value options, as the text each was given.</param>
/// <param name="Arguments">The operands, in the order they were written, each with its declared kind.</param>
/// <param name="Trailing">The words the command's own grammar owns, when its syntax allows them.</param>
public sealed record ShellCommandLine(
    IReadOnlySet<char> Flags,
    IReadOnlyDictionary<char, string> Values,
    IReadOnlyList<ShellArgument> Arguments,
    IReadOnlyList<ShellWord> Trailing) {

    /// <summary>Whether a flag was given.</summary>
    /// <param name="flag">The letter after the dash.</param>
    public bool Has(char flag) => Flags.Contains(flag);

    /// <summary>The text a value option was given, or null when it was not given.</summary>
    /// <param name="letter">The letter after the dash.</param>
    public string? Value(char letter) => Values.TryGetValue(letter, out string? value) ? value : null;

    /// <summary>The number a value option was given, or null when it was not given or is not a number.</summary>
    /// <param name="letter">The letter after the dash.</param>
    public int? Number(char letter) =>
        int.TryParse(Value(letter), NumberStyles.None, CultureInfo.InvariantCulture, out int value) ? value : null;

    /// <summary>The operands of one kind, in the order they were written.</summary>
    /// <param name="kind">The kind to take.</param>
    public IReadOnlyList<ShellWord> Words(ShellOperandKind kind) =>
        [.. Arguments.Where(argument => argument.Kind == kind).Select(argument => argument.Word)];
}

/// <summary>What a command accepts on its command line.</summary>
/// <remarks>
/// A command declares its flags, its value options and its operands once, and <see cref="Match"/> is
/// the only thing that turns a line into them: flags may cluster (<c>-lR</c>), a value may attach
/// (<c>-n5</c>) or take the next word, <c>--</c> ends the options, options must lead, and the operand
/// counts are checked against what was declared. Nothing here knows what a command is called except
/// through the usage line it is given to quote back.
/// </remarks>
/// <param name="Flags">The letters that are flags.</param>
/// <param name="Values">The letters that take a number.</param>
/// <param name="Operands">The operands, in the order they are written.</param>
/// <param name="Trailing">
/// Whether the command's own grammar continues past its operands, so that words beginning with a dash
/// are its language rather than a mistake. <c>find</c>'s expression is the reason it exists.
/// </param>
public sealed record ShellSyntax(
    IReadOnlyList<char> Flags,
    IReadOnlyList<ShellValueOption> Values,
    IReadOnlyList<ShellOperand> Operands,
    bool Trailing = false) {

    /// <summary>A command that takes nothing.</summary>
    public static ShellSyntax Nothing { get; } = new([], [], []);

    /// <summary>Matches a command line against this syntax.</summary>
    /// <param name="words">The words of the line, after the command's name.</param>
    /// <param name="usage">The command's usage line, quoted in a refusal.</param>
    public ShellMatchResult Match(IReadOnlyList<ShellWord> words, string usage) {
        ArgumentNullException.ThrowIfNull(words);
        ArgumentNullException.ThrowIfNull(usage);

        HashSet<char> flags = [];
        Dictionary<char, string> values = [];
        int index = 0;
        bool options = true;
        bool terminated = false;

        while (index < words.Count) {
            string text = words[index].Text;
            if (options && text == "--") {
                options = false;
                terminated = true;
                index++;
                continue;
            }
            if (!options || text.Length < 2 || text[0] != '-') {
                break;
            }

            int position = 1;
            while (position < text.Length) {
                char letter = text[position];
                if (Flags.Contains(letter)) {
                    _ = flags.Add(letter);
                    position++;
                    continue;
                }

                ShellValueOption? value = Values.FirstOrDefault(item => item.Letter == letter);
                if (value is null) {
                    return Refused(usage, $"there is no '-{letter}'");
                }

                string? given = position + 1 < text.Length
                    ? text[(position + 1)..]
                    : index + 1 < words.Count ? words[index + 1].Text : null;
                if (given is null || (value.Numeric && !int.TryParse(given, NumberStyles.None, CultureInfo.InvariantCulture, out _))) {
                    return Refused(usage, $"-{letter} needs {value.Name}");
                }

                values[letter] = given;
                if (position + 1 >= text.Length) {
                    index++;
                }
                position = text.Length;
            }

            index++;
        }

        List<ShellWord> operands = [.. words.Skip(index)];
        List<ShellWord> trailing = [];
        if (Trailing) {
            int expression = operands.FindIndex(word => word.Text.Length > 1 && word.Text[0] == '-');
            if (expression >= 0) {
                trailing = [.. operands[expression..]];
                operands = [.. operands[..expression]];
            }
        } else if (!terminated && operands.Any(word => word.Text.Length > 1 && word.Text[0] == '-')) {
            return Refused(usage, "an option must come before the operands");
        }

        int minimum = Operands.Sum(operand => operand.Minimum);
        int maximum = 0;
        foreach (ShellOperand operand in Operands) {
            maximum = operand.Maximum > int.MaxValue - maximum ? int.MaxValue : maximum + operand.Maximum;
        }

        if (operands.Count < minimum || operands.Count > maximum) {
            return Refused(usage, minimum == maximum
                ? $"it needs {Count(minimum)}"
                : $"it needs {Count(minimum)} and takes at most {Count(maximum)}");
        }

        List<ShellArgument> arguments = [];
        int left = operands.Count;
        int at = 0;
        for (int position = 0; position < Operands.Count; position++) {
            ShellOperand operand = Operands[position];
            int after = Operands.Skip(position + 1).Sum(item => item.Minimum);
            int take = Math.Clamp(left - after, operand.Minimum, operand.Maximum);
            for (int count = 0; count < take; count++) {
                arguments.Add(new ShellArgument(operand.Kind, operands[at++]));
            }
            left -= take;
        }

        return new ShellMatchResult(new ShellCommandLine(flags, values, arguments, trailing), null);
    }

    private static ShellMatchResult Refused(string usage, string why) =>
        new(null, $"The shell command has the wrong arguments ({why}). Run help {Name(usage)} for the form it takes: {usage}.");

    private static string Name(string usage) => usage.Split(' ')[0];

    private static string Count(int value) => value switch {
        1 => "one operand",
        2 => "two operands",
        _ => string.Create(CultureInfo.InvariantCulture, $"{value} operands"),
    };
}
