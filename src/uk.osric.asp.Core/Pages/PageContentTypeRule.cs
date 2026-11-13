// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Pages;

/// <summary>What a page key's extension names: its media type and its script policy.</summary>
/// <param name="MediaType">The response content type, including any parameters.</param>
/// <param name="ScriptPolicy">The response's script policy; <see cref="PageScriptPolicy.None"/> by default.</param>
public sealed record PageContentTypeRule(
    string MediaType,
    PageScriptPolicy ScriptPolicy = PageScriptPolicy.None);
