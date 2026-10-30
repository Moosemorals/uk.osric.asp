// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core;

/// <summary>Text to show a visitor. A value, not an entity: two equal greetings are interchangeable.</summary>
public readonly record struct Greeting(string Text);
