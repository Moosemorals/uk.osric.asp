// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Configuration;

/// <summary>Configuration the site cannot run without, reported once at startup.</summary>
/// <remarks>
/// It means "the operator has to change a setting", not "the site is broken", so the process writes
/// the message to standard error and exits rather than logging a stack trace.
/// </remarks>
internal sealed class ConfigurationException(string message) : Exception(message);
