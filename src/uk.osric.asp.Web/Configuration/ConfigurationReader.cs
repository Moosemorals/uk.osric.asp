// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Configuration;

using Microsoft.Extensions.Configuration;
using System.Globalization;

/// <summary>Reads one configuration section, collecting every problem before it reports.</summary>
/// <remarks>
/// <para>
/// Configuration is the only source of a policy value: there is no fallback in code, so a missing key
/// stops the site at startup instead of quietly becoming a build-time default. A reader collects the
/// whole section's problems — a missing key, a value of the wrong type, a value outside its range —
/// and <see cref="Complete"/> throws one <see cref="ConfigurationException"/> naming each, with the
/// range and the value this repository ships. The recommendation is guidance for the operator, not a
/// fallback: the value the site runs with is the one in <c>appsettings.json</c>.
/// </para>
/// <para>
/// A nested reader shares the problem list, so one <see cref="Complete"/> at the top of a factory
/// reports a whole section's worth of mistakes in one start.
/// </para>
/// </remarks>
internal sealed class ConfigurationReader {

    private readonly IConfiguration _configuration;
    private readonly List<string> _problems;

    /// <summary>Opens a section.</summary>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="section">The section's path, such as <c>Pages:Render</c>.</param>
    public ConfigurationReader(IConfiguration configuration, string section) {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(section);

        _configuration = configuration;
        Section = section;
        _problems = [];
    }

    private ConfigurationReader(IConfiguration configuration, string section, List<string> problems) {
        _configuration = configuration;
        Section = section;
        _problems = problems;
    }

    /// <summary>The section this reader was opened on.</summary>
    public string Section { get; }

    /// <summary>Opens a section below this one, sharing the problem list.</summary>
    /// <param name="key">The section's key.</param>
    public ConfigurationReader For(string key) {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        return new ConfigurationReader(_configuration, $"{Section}:{key}", _problems);
    }

    /// <summary>Opens another section by its full path, sharing the problem list.</summary>
    /// <param name="section">The section's path from the configuration root.</param>
    /// <remarks>For a value one section borrows from another, such as a session key's length.</remarks>
    public ConfigurationReader At(string section) {
        ArgumentException.ThrowIfNullOrWhiteSpace(section);

        return new ConfigurationReader(_configuration, section, _problems);
    }

    /// <summary>Whether this section exists at all.</summary>
    public bool Exists() => _configuration.GetSection(Section).Exists();

    /// <summary>The section's own value, for a setting written as a string rather than a set of keys.</summary>
    public string? ValueOrNull() => _configuration.GetSection(Section).Value;

    /// <summary>Records a problem that the reader's own values could not express.</summary>
    /// <param name="key">The key it belongs to, relative to this section.</param>
    /// <param name="problem">The sentence, without a leading key and without a full stop.</param>
    public void Fail(string key, string problem) =>
        _problems.Add($"{PathOf(key)} {problem}{(problem.EndsWith('.') ? string.Empty : ".")}");

    /// <summary>Records a problem that already names its key, such as a validator's complaint.</summary>
    /// <param name="problem">The whole sentence.</param>
    public void Fail(string problem) => _problems.Add(problem);

    /// <summary>Records a problem when a condition holds.</summary>
    /// <param name="condition">The condition that makes the configuration invalid.</param>
    /// <param name="key">The key it belongs to, relative to this section.</param>
    /// <param name="problem">The sentence, without a leading key and without a full stop.</param>
    public void FailIf(bool condition, string key, string problem) {
        if (condition) {
            Fail(key, problem);
        }
    }

    /// <summary>Reports every problem collected so far, or returns.</summary>
    /// <exception cref="ConfigurationException">One or more values are missing or unusable.</exception>
    public void Complete() {
        if (_problems.Count == 0) {
            return;
        }

        string lines = string.Join(Environment.NewLine + "  ", _problems);
        throw new ConfigurationException(
            $"The site cannot start because its configuration is incomplete or invalid:{Environment.NewLine}  {lines}"
            + $"{Environment.NewLine}Set these values in appsettings.json, appsettings.<Environment>.json or an environment variable, then start the site again.");
    }

    /// <summary>A required string, such as a key or a host.</summary>
    /// <param name="key">The key, relative to this section.</param>
    /// <param name="example">What a good value looks like, for the message.</param>
    /// <param name="allowEmpty">Whether a blank value is a meaningful setting.</param>
    public string Text(string key, string example, bool allowEmpty = false) {
        string? value = Raw(key);
        string advice = $"Set it to {example}";

        if (value is null) {
            Fail(key, $"is not set. {advice}.");
            return string.Empty;
        }

        if (!allowEmpty && string.IsNullOrWhiteSpace(value)) {
            Fail(key, $"is blank. {advice}.");
            return string.Empty;
        }

        return value;
    }

    /// <summary>A string that may be absent, for a value the site can genuinely do without.</summary>
    /// <param name="key">The key, relative to this section.</param>
    public string? OptionalText(string key) => Raw(key);

    /// <summary>A required section that is a value rather than a set of keys.</summary>
    /// <param name="example">What a good value looks like, for the message.</param>
    /// <param name="allowEmpty">Whether a blank value is a meaningful setting.</param>
    public string Value(string example, bool allowEmpty = false) {
        string advice = $"Set it to {example}";
        string? value = _configuration.GetSection(Section).Value;

        if (value is null) {
            _problems.Add($"{Section} is not set. {advice}.");
            return string.Empty;
        }

        if (!allowEmpty && string.IsNullOrWhiteSpace(value)) {
            _problems.Add($"{Section} is blank. {advice}.");
            return string.Empty;
        }

        return value;
    }

    /// <summary>A required whole number, bounded so a mistake cannot switch the limit off.</summary>
    /// <param name="key">The key, relative to this section.</param>
    /// <param name="recommended">The value this repository ships, for the message.</param>
    /// <param name="minimum">The smallest value the site will run with.</param>
    /// <param name="maximum">The largest value the site will run with.</param>
    public int Number(string key, int recommended, int minimum, int maximum) {
        string advice = $"Set it to a whole number from {minimum} to {maximum}; {recommended} is recommended.";
        string outside = $"the accepted range of {minimum} to {maximum}";
        string? raw = Missing(key, advice);

        if (raw is null) {
            return recommended;
        }

        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)) {
            Fail(key, $"is \"{raw}\", which is not a whole number. {advice}");
            return recommended;
        }

        if (value < minimum || value > maximum) {
            Fail(key, $"is {value}, which is outside {outside}. {advice}");
            return recommended;
        }

        return value;
    }

    /// <summary>A required whole number too large for <see cref="int"/>.</summary>
    /// <param name="key">The key, relative to this section.</param>
    /// <param name="recommended">The value this repository ships, for the message.</param>
    /// <param name="minimum">The smallest value the site will run with.</param>
    /// <param name="maximum">The largest value the site will run with.</param>
    public long Number(string key, long recommended, long minimum, long maximum) {
        string advice = $"Set it to a whole number from {minimum} to {maximum}; {recommended} is recommended.";
        string outside = $"the accepted range of {minimum} to {maximum}";
        string? raw = Missing(key, advice);

        if (raw is null) {
            return recommended;
        }

        if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value)) {
            Fail(key, $"is \"{raw}\", which is not a whole number. {advice}");
            return recommended;
        }

        if (value < minimum || value > maximum) {
            Fail(key, $"is {value}, which is outside {outside}. {advice}");
            return recommended;
        }

        return value;
    }

    /// <summary>A required fraction, such as a budget that may be part of a second.</summary>
    /// <param name="key">The key, relative to this section.</param>
    /// <param name="recommended">The value this repository ships, for the message.</param>
    /// <param name="minimum">The smallest value the site will run with.</param>
    /// <param name="maximum">The largest value the site will run with.</param>
    public double Decimal(string key, double recommended, double minimum, double maximum) {
        string advice = $"Set it to a number from {Show(minimum)} to {Show(maximum)}; {Show(recommended)} is recommended.";
        string outside = $"the accepted range of {Show(minimum)} to {Show(maximum)}";
        string? raw = Missing(key, advice);

        if (raw is null) {
            return recommended;
        }

        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            || !double.IsFinite(value)) {
            Fail(key, $"is \"{raw}\", which is not a number. {advice}");
            return recommended;
        }

        if (value < minimum || value > maximum) {
            Fail(key, $"is {Show(value)}, which is outside {outside}. {advice}");
            return recommended;
        }

        return value;
    }

    /// <summary>A required switch.</summary>
    /// <param name="key">The key, relative to this section.</param>
    /// <param name="recommended">The value this repository ships, for the message.</param>
    public bool Switch(string key, bool recommended) {
        string advice = $"Set it to true or false; {recommended.ToString().ToLowerInvariant()} is recommended.";
        string? raw = Missing(key, advice);

        if (raw is null) {
            return recommended;
        }

        if (!bool.TryParse(raw, out bool value)) {
            Fail(key, $"is \"{raw}\", which is not true or false. {advice}");
            return recommended;
        }

        return value;
    }

    /// <summary>A required count of minutes, bounded like a whole number.</summary>
    /// <param name="key">The key, relative to this section.</param>
    /// <param name="recommended">The value this repository ships, in minutes, for the message.</param>
    /// <param name="minimum">The smallest value, in minutes, the site will run with.</param>
    /// <param name="maximum">The largest value, in minutes, the site will run with.</param>
    public TimeSpan Minutes(string key, int recommended, int minimum, int maximum) =>
        TimeSpan.FromMinutes(Number(key, recommended, minimum, maximum));

    /// <summary>A required list of strings, such as the keys the site's own routes answer.</summary>
    /// <param name="key">The key, relative to this section.</param>
    /// <param name="description">What the entries are, for the message.</param>
    /// <param name="minimum">The fewest entries the site will run with.</param>
    public IReadOnlyList<string> List(string key, string description, int minimum) {
        string[]? values = _configuration.GetSection(Section).GetSection(key).Get<string[]>();
        string advice = $"Add the section as an array of {description}; at least {minimum} is required.";

        if (values is null) {
            Fail(key, $"is not set. {advice}");
            return [];
        }

        string[] kept = [.. values.Where(value => !string.IsNullOrWhiteSpace(value))];
        if (kept.Length < minimum) {
            Fail(key, $"has {kept.Length} entr{(kept.Length == 1 ? "y" : "ies")}. {advice}");
            return [];
        }

        return kept;
    }

    private static string Show(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private string PathOf(string key) => $"{Section}:{key}";

    private string? Raw(string key) => _configuration.GetSection(Section)[key];

    private string? Missing(string key, string advice) {
        string? value = Raw(key);
        if (value is null) {
            Fail(key, $"is not set. {advice}");
        }

        return value;
    }
}
