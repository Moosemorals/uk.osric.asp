// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

/// <summary>One document the shell renders, named so a failure says which view it was.</summary>
/// <param name="Name">The view, as the test report names it.</param>
/// <param name="Html">The rendered document.</param>
internal sealed record ShellView(string Name, string Html);
