// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>The languages a check answers for.</summary>
/// <remarks>
/// <para>
/// The set is closed on purpose: a check names one of these, and the host refuses a name that is not
/// one of them rather than guessing from a key's extension. A key's extension is a content type, not
/// a validator: a <c>.html</c> page can hold a template or an asset, and a <c>.js</c> page is a
/// template with JavaScript in it until its constructs are blanked.
/// </para>
/// <para>
/// One declaration feeds the author manual, the assistant's tool schema and the refusal, so a caller
/// never reads a second list. The names a caller passes are <see cref="PageCheckLanguages.Names"/>.
/// </para>
/// </remarks>
public enum PageCheckLanguage {

    /// <summary>A page template: generation, the page API policy and the compiler.</summary>
    Template,

    /// <summary>
    /// A template's literal text, parsed as an ES module: the JavaScript a browser would be served.
    /// </summary>
    JavaScript,
}
