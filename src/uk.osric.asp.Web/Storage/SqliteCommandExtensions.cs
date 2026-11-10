// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Storage;

using System.Data.Common;
using System.Globalization;

/// <summary>Parameters for the stores' hand-written statements.</summary>
/// <remarks>
/// The conversation store and the memory store both run SQL themselves, and a timestamp has to be
/// written the way every table stores one rather than left to the provider's default rendering.
/// </remarks>
internal static class SqliteCommandExtensions {

    /// <summary>Adds a parameter, with a timestamp written as round-trip text.</summary>
    /// <param name="command">The command to add it to.</param>
    /// <param name="name">The parameter's name, sigil included.</param>
    /// <param name="value">The value to bind.</param>
    public static void AddParameter(this DbCommand command, string name, object value) {
        ArgumentNullException.ThrowIfNull(command);

        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value switch {
            DateTimeOffset timestamp => timestamp.ToString("O", CultureInfo.InvariantCulture),
            _ => value,
        };
        command.Parameters.Add(parameter);
    }
}
